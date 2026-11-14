using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapGetRepoConfig(IEndpointRouteBuilder g)
    {
        g.MapGet("", async (IFdroidRepository db, CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            return Results.Ok(cfg);
        }).RequireAuthorization("InteractiveOnly")
            .Produces<RepoConfig>();
    }
}
