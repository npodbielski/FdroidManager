using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Endpoints.Auth;

public static partial class AuthEndpoints
{
    private static void MapAuthMe(IEndpointRouteBuilder g)
    {
        g.MapGet("/me", (HttpContext http) =>
        {
            var user = http.User;
            var username = user.Identity?.Name ?? "";
            var roles = user.FindAll("roles").Select(c => c.Value).Distinct().ToArray();
            var scopes = user.FindAll(Scopes.ClaimType).Select(c => c.Value).Distinct().ToArray();
            var avatar = user.FindFirst("picture")?.Value;
            return Results.Ok(new AuthMeResponse(username, roles, scopes, avatar));
        }).RequireAuthorization(Scopes.Read)
            .Produces<AuthMeResponse>();
    }
}
