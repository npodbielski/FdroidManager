using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IKeyService
{
    Task<AppKey> GenerateKeyAsync(string packageName, string? branch = null, CancellationToken ct = default);

    Task<AppKey> ImportKeyAsync(string packageName, Stream keystoreStream,
        string storePassword, string keyPassword, string alias, string? branch = null, CancellationToken ct = default);

    Task<(AppKey Key, string Password)> GetAvailableKeyAsync(string packageName, string? branch = null, CancellationToken ct = default);

    Task SignApkAsync(string packageName, string unsignedPath, string signedPath, string? branch = null, CancellationToken ct = default);
}
