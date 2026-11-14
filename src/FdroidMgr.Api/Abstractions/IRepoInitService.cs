using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IRepoInitService
{
    string RepoRoot { get; }

    bool IsInitialized();

    string ResolveRepoUrl(string? storedUrl = null);

    Task<RepoInitResult> InitAsync(RepoInitRequest req, CancellationToken ct = default);
}
