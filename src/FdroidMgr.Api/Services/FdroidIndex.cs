namespace FdroidMgr.Api.Services;

public record FdroidIndex(
    string RepoName,
    string? RepoUrl,
    long Timestamp,
    IReadOnlyList<FdroidApp> Apps);
