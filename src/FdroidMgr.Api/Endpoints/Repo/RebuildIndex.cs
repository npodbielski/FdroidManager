using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapRebuildIndex(IEndpointRouteBuilder g)
    {
        g.MapPost("/index/rebuild", async (IFdroidRepository db, IRepoIndexService index, CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            await index.GenerateAsync(cfg, ct: ct);
            return Results.Ok(new StatusResponse("ok"));
        }).RequireAuthorization("InteractiveOnly")
            .Produces<StatusResponse>();
    }
}
