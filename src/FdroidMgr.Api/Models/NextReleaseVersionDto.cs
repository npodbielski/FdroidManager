
namespace FdroidMgr.Api.Models;

public record NextReleaseVersionDto
{
    public string PackageName { get; init; } = "";

    public string? PreviousVersionName { get; init; }

    public int? PreviousVersionCode { get; init; }

    public string VersionName { get; init; } = "1.0.0";

    public int VersionCode { get; init; } = 1;
}
