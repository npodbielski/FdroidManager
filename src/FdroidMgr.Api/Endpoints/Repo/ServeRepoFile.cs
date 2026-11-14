using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapServeRepoFile(IEndpointRouteBuilder app)
    {
        app.MapGet("/fdroid/repo/{**file}", async (string? file, IFdroidRepository db, IDownloadStatsService stats, HttpContext http, IOService io, IRepoStageService stages, IRepoVisibility visibility, IDownloadStatsQueue queue, ILoggerFactory logs, CancellationToken ct) =>
        {
            var cfg = await db.FindRepoConfigAsync(ct);
            var output = cfg?.RepoPath ?? "repo";
            var served = stages.ResolvePublicDir() ?? output;
            var full = string.IsNullOrEmpty(file)
                ? Path.Combine(served, "index.xml")
                : Path.GetFullPath(Path.Combine(served, file.Replace('/', Path.DirectorySeparatorChar)));

            if (!visibility.IsWithin(full, served) || visibility.IsDeniedPath(visibility.RelativeTo(served, full)) || !io.FileExists(full))
            {
                return Results.NotFound();
            }

            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == ".apk" && cfg is not null)
            {
                try
                {
                    var (pkg, vc) = await stats.ResolveAsync(Path.GetFileName(full), cfg, ct);
                    if (pkg is not null && vc is not null)
                    {
                        queue.TryEnqueue(pkg, vc.Value);
                    }
                }
                catch (IOException ex)
                {
                    Log.DownloadAttributionFailed(logs.CreateLogger(nameof(RepoEndpoints)), ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.DownloadAttributionFailed(logs.CreateLogger(nameof(RepoEndpoints)), ex);
                }
            }

            http.Response.Headers["X-Content-Type-Options"] = "nosniff";

            return Results.File(full, ContentType(ext), enableRangeProcessing: true);
        }).RequireAuthorization("repo:read");
    }
}
