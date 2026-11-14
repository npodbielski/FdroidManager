using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IRepoIndexService
{
    Task GenerateAsync(RepoConfig cfg, bool createMetadata = false, IRepoLockLease? lease = null, CancellationToken ct = default);

    Task GenerateStrictAsync(RepoConfig cfg, bool createMetadata = false, IRepoLockLease? lease = null, CancellationToken ct = default);
}
