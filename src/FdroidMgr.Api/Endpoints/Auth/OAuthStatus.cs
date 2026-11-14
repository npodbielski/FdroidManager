
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Auth;

public static partial class AuthEndpoints
{
    private static void MapOAuthStatus(IEndpointRouteBuilder g)
    {
        g.MapGet("/oauth/status", (IOAuthConfigService oauth, ISignInDiagnostics signIn) =>
        {
            var status = oauth.GetStatus();
            var last = signIn.Last;
            status.LastSignInError = last?.Message;
            status.LastSignInErrorUtc = last?.Utc;
            return Results.Ok(status);
        }).AllowAnonymous()
            .Produces<OAuthConfigStatus>();
    }
}
