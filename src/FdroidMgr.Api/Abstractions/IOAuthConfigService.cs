using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IOAuthConfigService
{
    bool IsConfigured();

    OAuthConfigStatus GetStatus();

    Task<OAuthConfigStatus> ConfigureAsync(OAuthConfigRequest req, CancellationToken ct = default);
}
