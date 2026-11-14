using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public class FdroidClientKeyService(FdroidDb db, IPasswordProtector protector, IApiKeyService apiKeys)
    : IFdroidClientKeyService
{

    public const string KeyName = "F-Droid client";

    public static readonly string[] ClientKeyScopes = [Scopes.Read];

    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    public async Task<FdroidClientKey> GetOrCreateAsync(ApiUser user, CancellationToken ct = default)
    {
        var existing = await db.FdroidClientKey.FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return existing;
        }

        await CreateLock.WaitAsync(ct);
        try
        {
            existing = await db.FdroidClientKey.FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                return existing;
            }

            var (hash, prefix, cipher) = NewSecretParts();
            var key = new FdroidClientKey
            {
                UserId = user.Id,
                KeyHash = hash,
                KeyPrefix = prefix,
                SecretCipher = cipher,
            };
            db.FdroidClientKey.Add(key);
            await db.SaveChangesAsync(ct);
            return key;
        }
        finally
        {
            CreateLock.Release();
        }
    }

    public async Task<(FdroidClientKey Key, string Secret)> RevealAsync(CancellationToken ct = default)
    {
        var key = await db.FdroidClientKey.FirstAsync(ct);
        key.LastRevealedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (key, Decrypt(key));
    }

    public async Task<(FdroidClientKey Key, string Secret)> RegenerateAsync(CancellationToken ct = default)
    {
        var key = await db.FdroidClientKey.FirstAsync(ct);
        var (hash, prefix, cipher) = NewSecretParts();
        key.KeyHash = hash;
        key.KeyPrefix = prefix;
        key.SecretCipher = cipher;
        key.RegeneratedUtc = DateTime.UtcNow;
        key.LastRevealedUtc = DateTime.UtcNow;
        key.LastUsedUtc = null;
        await db.SaveChangesAsync(ct);
        return (key, Decrypt(key));
    }

    private (string Hash, string Prefix, string Cipher) NewSecretParts()
    {
        var secret = apiKeys.GenerateSecret();
        return (
            apiKeys.Hash(secret),
            apiKeys.Prefix(secret),
            protector.Protect(secret));
    }

    private string Decrypt(FdroidClientKey key)
        => protector.Unprotect(key.SecretCipher);
}
