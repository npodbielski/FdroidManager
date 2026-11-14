using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

public interface IApiKeyService
{
    string GenerateSecret();

    string Hash(string secret);

    string BuildBasicAuthUrl(string repoUrl, string username, string key);

    string Prefix(string secret);

    Task<ApiUser> GetOrCreateUserAsync(string subject, string username, CancellationToken ct = default);

    Task<(ApiKey Key, string Plaintext)> CreateAsync(ApiUser user, string name,
        IReadOnlyCollection<string> scopes, int? expiresDays, CancellationToken ct = default);

    Task<ApiKeyPrincipal?> ValidateAsync(string rawSecret, CancellationToken ct = default);

    Task<bool> RevokeAsync(int id, string subject, CancellationToken ct = default);
}
