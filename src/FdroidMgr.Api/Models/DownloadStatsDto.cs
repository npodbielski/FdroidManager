
namespace FdroidMgr.Api.Models;

public record DownloadStatsDto
{
    public long TotalDownloads { get; init; }

    public IReadOnlyList<AppDownloadStat> Apps { get; init; } = [];

    public IReadOnlyList<ReleaseDownloadStat> Releases { get; init; } = [];

    public IReadOnlyList<DailyDownloadStat> Daily { get; init; } = [];
}
