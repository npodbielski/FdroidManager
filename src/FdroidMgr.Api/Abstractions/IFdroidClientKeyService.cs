using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IFdroidClientKeyService
{
    Task<FdroidClientKey> GetOrCreateAsync(ApiUser user, CancellationToken ct = default);

    Task<(FdroidClientKey Key, string Secret)> RevealAsync(CancellationToken ct = default);

    Task<(FdroidClientKey Key, string Secret)> RegenerateAsync(CancellationToken ct = default);
}
