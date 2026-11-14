using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IReleaseService
{
    Task<ReleaseDto> PublishAsync(string packageName, IFormFile apk, string? changelog, string? branch = null, CancellationToken ct = default);
}
