
namespace FdroidMgr.Api.Models;

public record AppDownloadStat
{
    public string PackageName { get; init; } = "";

    public string? Name { get; init; }

    public long Downloads { get; init; }

    public int? LatestVersionCode { get; init; }
}
