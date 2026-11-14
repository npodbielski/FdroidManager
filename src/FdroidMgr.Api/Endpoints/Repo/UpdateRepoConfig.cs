using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapUpdateRepoConfig(IEndpointRouteBuilder g)
    {
        g.MapPut("", async (RepoConfig req, IFdroidRepository db, IRepoIndexService index, CancellationToken ct) =>
        {
            var cfg = await db.TrackRepoConfigAsync(ct);

            if (!string.IsNullOrEmpty(req.RepoUrl))
            {
                cfg.RepoUrl = req.RepoUrl;
            }

            if (!string.IsNullOrEmpty(req.RepoName))
            {
                cfg.RepoName = req.RepoName;
            }

            if (!string.IsNullOrEmpty(req.RepoDescription))
            {
                cfg.RepoDescription = req.RepoDescription;
            }

            if (!string.IsNullOrEmpty(req.RepoPath))
            {
                cfg.RepoPath = req.RepoPath;
            }

            if (!string.IsNullOrEmpty(req.KeyPath))
            {
                cfg.KeyPath = req.KeyPath;
            }

            if (req.OAuthIssuer is not null)
            {
                cfg.OAuthIssuer = req.OAuthIssuer;
            }

            if (req.OAuthAudience is not null)
            {
                cfg.OAuthAudience = req.OAuthAudience;
            }

            await db.SaveChangesAsync(ct);
            await index.GenerateAsync(cfg, ct: ct);
            return Results.Ok(cfg);
        }).RequireAuthorization("InteractiveOnly")
            .Produces<RepoConfig>();
    }
}
