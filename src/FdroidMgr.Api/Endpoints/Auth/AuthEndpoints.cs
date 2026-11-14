
namespace FdroidMgr.Api.Endpoints.Auth;

public static partial class AuthEndpoints
{
    public static void MapAuthApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");
        MapAuthLogin(g);
        MapAuthLogout(g);
        MapAuthMe(g);
        MapOAuthStatus(g);
        MapConfigureOAuth(g);

    }
}
