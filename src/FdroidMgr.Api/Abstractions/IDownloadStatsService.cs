using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IDownloadStatsService
{
    Task<(string? Package, int? VersionCode)> ResolveAsync(string apkFileName, RepoConfig cfg, CancellationToken ct = default);

    Task RecordAsync(string? package, int? versionCode, CancellationToken ct = default);

    Task<DownloadStatsDto> GetAsync(RepoConfig cfg, CancellationToken ct = default);
}
