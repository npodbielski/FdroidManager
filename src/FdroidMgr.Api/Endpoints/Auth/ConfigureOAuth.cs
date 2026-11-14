using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Auth;

public static partial class AuthEndpoints
{
    private static void MapConfigureOAuth(IEndpointRouteBuilder g)
    {
        g.MapPost("/oauth", async (OAuthConfigRequest req, IOAuthConfigService oauth, CancellationToken ct) =>
        {
            var status = await oauth.ConfigureAsync(req, ct);
            return Results.Json(status, statusCode: StatusCodes.Status201Created);
        }).AllowAnonymous()
            .Produces<OAuthConfigStatus>(StatusCodes.Status201Created);
    }
}
