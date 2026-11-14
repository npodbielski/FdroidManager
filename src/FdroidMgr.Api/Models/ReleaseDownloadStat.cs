
namespace FdroidMgr.Api.Models;

public record ReleaseDownloadStat
{
    public string PackageName { get; init; } = "";

    public int VersionCode { get; init; }

    public string? VersionName { get; init; }

    public long Downloads { get; init; }
}
