using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Stats;

public static partial class StatsEndpoints
{
    private static void MapGetDownloadStats(IEndpointRouteBuilder g)
    {
        g.MapGet("/downloads", async (IDownloadStatsService stats, IFdroidRepository db, CancellationToken ct) =>
        {
            var cfg = await db.FindRepoConfigAsync(ct);
            return cfg is null ? Results.Ok(new DownloadStatsDto()) : Results.Ok(await stats.GetAsync(cfg, ct));
        }).Produces<DownloadStatsDto>();
    }
}
