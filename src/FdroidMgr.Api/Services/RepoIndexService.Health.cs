using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class RepoIndexService
{
    public static string ResolveRepoRoot(RepoConfig cfg)
        => ResolveRepoRoot(cfg, new EnvironmentService());

    public static string ResolveRepoRoot(RepoConfig cfg, IEnvironmentService env)
    {
        var env2 = env.GetEnvironmentVariable("FDROID_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(env2))
        {
            return env2;
        }

        if (string.IsNullOrWhiteSpace(cfg.RepoPath))
        {
            return "/data/fdroidrepo";
        }

        var parent = Path.GetDirectoryName(cfg.RepoPath.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            return parent;
        }

        return "/data/fdroidrepo";
    }

    private async Task RecordOutcomeAsync(bool success, string? error, CancellationToken ct)
    {
        try
        {
            var row = await db.RepoConfig.FirstAsync(ct);
            if (success)
            {
                row.LastIndexBuiltUtc = DateTime.UtcNow;
                row.LastIndexError = null;
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                row.LastIndexError = Trim(error);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Log.IndexOutcomeRecordFailed(log, ex);
        }
    }
}
