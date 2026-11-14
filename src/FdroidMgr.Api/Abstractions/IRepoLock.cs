
namespace FdroidMgr.Api.Abstractions;

public interface IRepoLockLease : IDisposable;

public interface IRepoLock
{
    ValueTask<IRepoLockLease> AcquireAsync(CancellationToken ct = default);
}
