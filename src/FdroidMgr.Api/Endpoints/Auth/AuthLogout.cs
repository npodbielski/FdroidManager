using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace FdroidMgr.Api.Endpoints.Auth;

public static partial class AuthEndpoints
{
    private static void MapAuthLogout(IEndpointRouteBuilder g)
    {
        g.MapPost("/logout", () => Results.SignOut(authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
            .RequireAuthorization();
    }
}
