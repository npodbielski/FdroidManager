namespace FdroidMgr.Api.Models;

public record AppListItem(
    string PackageName,
    string Name,
    string Summary,
    string License,
    string? Icon,
    int VersionCount,
    string? LatestVersion,
    int? LatestVersionCode);
