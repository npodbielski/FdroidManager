
namespace FdroidMgr.Api.Endpoints.FdroidClientKey;

public static partial class FdroidClientKeyEndpoints
{
    public static void MapFdroidClientKeyApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/fdroid-client-key").RequireAuthorization("InteractiveOnly");
        MapGetFdroidClientKey(g);
        MapRevealFdroidClientKey(g);
        MapRegenerateFdroidClientKey(g);

    }
}
