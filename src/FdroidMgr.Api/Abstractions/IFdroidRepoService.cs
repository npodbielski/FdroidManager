using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

public interface IFdroidRepoService
{
    Task<FdroidIndex?> ReadIndexAsync(RepoConfig cfg, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> ReadV2IconsAsync(RepoConfig cfg, CancellationToken ct = default);

    Task<string?> ReadChangelogAsync(RepoConfig cfg, string packageName, int versionCode, CancellationToken ct = default);

    string? ReadChangelog(RepoConfig cfg, string packageName, int versionCode);

    Task WriteChangelogAsync(RepoConfig cfg, string packageName, int versionCode, string changelog, CancellationToken ct = default);
}
