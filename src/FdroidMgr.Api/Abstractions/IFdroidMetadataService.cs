using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IFdroidMetadataService
{
    string ResolveRepoRoot(RepoConfig cfg);

    string? GetPath(string packageName, string? repoRoot);

    Task<bool> ExistsAsync(string packageName, CancellationToken ct = default);

    Task<string> WriteAsync(string packageName, AppMetadataFields fields, CancellationToken ct = default);

    Task<string?> ReadAsync(string packageName, CancellationToken ct = default);
}
