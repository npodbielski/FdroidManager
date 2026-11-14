using System.Security.Cryptography;
using System.Text;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed record ApiKeyPrincipal(int KeyId, string KeyName, string Subject, string Username, string[] Scopes);

public class ApiKeyService(FdroidDb db) : IApiKeyService
{
    /// <summary>
    /// A key used more recently than this still counts as seen; keeps one
    /// UPDATE per burst of client requests instead of one per request.
    /// </summary>
    internal static readonly TimeSpan LastUsedWriteThreshold = TimeSpan.FromSeconds(60);

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "fdmgr_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Hash(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public string BuildBasicAuthUrl(string repoUrl, string username, string key)
    {
        var schemeIdx = repoUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx < 0)
        {
            return repoUrl;
        }

        var prefix = repoUrl[..(schemeIdx + 3)];
        var rest = repoUrl[(schemeIdx + 3)..];
        var user = string.IsNullOrWhiteSpace(username) ? "fdroid" : username;
        return prefix + Uri.EscapeDataString(user) + ":" + key + "@" + rest;
    }

    public string Prefix(string secret)
        => secret.Length > 10 ? secret[..10] : secret;

    public string[] SplitScopes(string scopes)
        => scopes.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool ShouldStampLastUsed(DateTime? lastUsedUtc, DateTimeOffset nowUtc)
        => lastUsedUtc is null || nowUtc.UtcDateTime - lastUsedUtc.Value >= LastUsedWriteThreshold;

    public async Task<ApiUser> GetOrCreateUserAsync(string subject, string username, CancellationToken ct = default)
    {
        var user = await db.ApiUsers.FirstOrDefaultAsync(u => u.Subject == subject, ct);
        if (user is not null)
        {
            if (string.IsNullOrEmpty(username) || user.Username == username)
            {
                return user;
            }

            user.Username = username;
            await db.SaveChangesAsync(ct);
            return user;
        }

        user = new ApiUser { Subject = subject, Username = username, CreatedUtc = DateTime.UtcNow };
        db.ApiUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<(ApiKey Key, string Plaintext)> CreateAsync(ApiUser user, string name,
        IReadOnlyCollection<string> scopes, int? expiresDays, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("name is required");
        }

        if (scopes is null || scopes.Count == 0)
        {
            throw new ArgumentException("at least one scope is required");
        }

        var invalid = scopes.Where(s => !Scopes.IsValid(s)).ToArray();
        if (invalid.Length > 0)
        {
            throw new ArgumentException("invalid scope: " + string.Join(", ", invalid));
        }

        var plaintext = GenerateSecret();
        var expanded = Scopes.Expand(scopes);

        var key = new ApiKey
        {
            UserId = user.Id,
            Name = trimmed,
            KeyHash = Hash(plaintext),
            KeyPrefix = Prefix(plaintext),
            Scopes = string.Join(',', expanded),
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = expiresDays is > 0 ? DateTime.UtcNow.AddDays(expiresDays.Value) : null,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return (key, plaintext);
    }

    public async Task<ApiKeyPrincipal?> ValidateAsync(string rawSecret, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawSecret) || !rawSecret.StartsWith("fdmgr_", StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Hash(rawSecret);
        var key = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == hash)
            .Select(k => new
            {
                k.Id,
                k.Name,
                k.RevokedUtc,
                k.ExpiresUtc,
                k.Scopes,
                k.LastUsedUtc,
                k.User!.Subject,
                k.User!.Username,
            })
            .FirstOrDefaultAsync(ct);

        if (key is not null)
        {
            if (key.RevokedUtc is not null)
            {
                return null;
            }

            if (key.ExpiresUtc is not null && key.ExpiresUtc <= DateTime.UtcNow)
            {
                return null;
            }

            if (ShouldStampLastUsed(key.LastUsedUtc, DateTimeOffset.UtcNow))
            {
                await db.ApiKeys
                    .Where(k => k.Id == key.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedUtc, DateTime.UtcNow), ct);
            }

            return new ApiKeyPrincipal(key.Id, key.Name, key.Subject, key.Username, Scopes.Expand(SplitScopes(key.Scopes)));
        }

        var clientKey = await db.FdroidClientKey
            .AsNoTracking()
            .Where(k => k.KeyHash == hash)
            .Select(k => new
            {
                k.Id,
                k.LastUsedUtc,
                k.Creator!.Subject,
                k.Creator!.Username,
            })
            .FirstOrDefaultAsync(ct);

        if (clientKey is null)
        {
            return null;
        }

        if (ShouldStampLastUsed(clientKey.LastUsedUtc, DateTimeOffset.UtcNow))
        {
            await db.FdroidClientKey
                .Where(k => k.Id == clientKey.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedUtc, DateTime.UtcNow), ct);
        }

        return new ApiKeyPrincipal(clientKey.Id, FdroidClientKeyService.KeyName, clientKey.Subject, clientKey.Username, FdroidClientKeyService.ClientKeyScopes);
    }

    public async Task<bool> RevokeAsync(int id, string subject, CancellationToken ct = default)
    {
        var user = await db.ApiUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Subject == subject, ct);
        if (user is null)
        {
            return false;
        }

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == user.Id, ct);
        if (key is null || key.RevokedUtc is not null)
        {
            return false;
        }

        key.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
