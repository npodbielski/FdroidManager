using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IFdroidRepository
{
    Task<RepoConfig> GetRepoConfigAsync(CancellationToken ct = default);

    Task<RepoConfig?> FindRepoConfigAsync(CancellationToken ct = default);

    Task<RepoConfig> TrackRepoConfigAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<ApiUser?> FindUserBySubjectAsync(string subject, CancellationToken ct = default);

    Task<List<ApiKey>> ListApiKeysAsync(int userId, CancellationToken ct = default);

    Task<List<AppKey>> ListKeysAsync(string packageName, CancellationToken ct = default);

    Task<List<string>> ListKeyFingerprintsAsync(string packageName, CancellationToken ct = default);
}
