using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapRepoStatus(IEndpointRouteBuilder g)
    {
        g.MapGet("/status", async (IFdroidRepository db, IFdroidRepoService repo, CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            var repoIndex = await repo.ReadIndexAsync(cfg, ct);
            var appCount = repoIndex?.Apps.Count ?? 0;
            return Results.Ok(new RepoStatusResponse(
                cfg.RepoName,
                appCount,
                DateTime.UtcNow,
                cfg.LastIndexBuiltUtc,
                cfg.LastIndexError,
                cfg.LastIndexBuiltUtc is not null && cfg.LastIndexError is null));
        }).AllowAnonymous()
            .Produces<RepoStatusResponse>();
    }
}
