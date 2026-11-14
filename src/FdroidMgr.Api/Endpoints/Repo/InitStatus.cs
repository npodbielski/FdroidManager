using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapInitStatus(IEndpointRouteBuilder g)
    {
        g.MapGet("/init/status", async (IFdroidRepository db, IRepoInitService init, CancellationToken ct) =>
        {
            var cfg = await db.FindRepoConfigAsync(ct);
            var initialized = init.IsInitialized();
            return Results.Ok(new RepoInitStatus
            {
                Initialized = initialized,
                RepoPath = init.RepoRoot,
                Name = cfg?.RepoName,
                SuggestedRepoUrl = init.ResolveRepoUrl(cfg?.RepoUrl),
            });
        }).AllowAnonymous()
            .Produces<RepoInitStatus>();
    }
}
