using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Auth;

public static partial class AuthEndpoints
{
    private static void MapAuthLogin(IEndpointRouteBuilder g)
    {
        g.MapGet("/login", async (string? returnUrl, HttpContext http, IOAuthConfigService oauth) =>
        {
            if (!oauth.IsConfigured())
            {
                return Results.Redirect("/ui");
            }

            await http.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = OidcAuthExtensions.SignInSuccessTarget(returnUrl) });
            return Results.Empty;
        }).AllowAnonymous();
    }
}
