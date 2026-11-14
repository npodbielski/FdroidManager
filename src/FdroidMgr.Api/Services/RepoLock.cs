using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class RepoLock : IRepoLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask<IRepoLockLease> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IRepoLockLease
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose()
            => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
