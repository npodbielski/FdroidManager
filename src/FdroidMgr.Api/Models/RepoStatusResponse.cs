namespace FdroidMgr.Api.Models;

/// <summary>
/// Repository health as shown on the dashboard.
/// </summary>
public record RepoStatusResponse(
    string RepoName,
    int AppCount,
    DateTime UpdatedUtc,
    DateTime? LastIndexBuiltUtc,
    string? LastIndexError,
    bool IndexHealthy);
