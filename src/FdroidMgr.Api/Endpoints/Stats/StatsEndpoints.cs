
namespace FdroidMgr.Api.Endpoints.Stats;

public static partial class StatsEndpoints
{
    public static void MapStatsApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/stats").RequireAuthorization("InteractiveOnly");
        MapGetDownloadStats(g);

    }
}
