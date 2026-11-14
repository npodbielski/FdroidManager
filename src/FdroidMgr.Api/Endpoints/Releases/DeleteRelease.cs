using FdroidMgr.Api.Services;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Releases;

public static partial class ReleasesEndpoints
{
    private static void MapDeleteRelease(IEndpointRouteBuilder g)
    {
        g.MapDelete("/{versionCode:int}", async (string packageName, int versionCode, IFdroidRepository db,
            IRepoIndexService index, IFdroidRepoService repo, IOService io, ILoggerFactory logs,
            CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            var repoRoot = RepoIndexService.ResolveRepoRoot(cfg);
            var duplicatesRemoved = await RemoveDuplicateApksAsync(cfg, packageName, versionCode, io);

            var index1 = await repo.ReadIndexAsync(cfg, ct);
            var app0 = index1?.Apps.FirstOrDefault(a => a.PackageName == packageName);
            var version = app0?.Versions.FirstOrDefault(v => v.VersionCode == versionCode);

            if (version is not null)
            {
                var apkPath = Path.Combine(cfg.RepoPath, version.ApkName);
                if (io.FileExists(apkPath))
                {
                    io.DeleteFile(apkPath);
                }

                var changelogPath = FdroidRepoService.ChangelogPath(repoRoot, packageName, versionCode);
                if (io.FileExists(changelogPath))
                {
                    io.DeleteFile(changelogPath);
                }
            }

            try
            {
                await index.GenerateStrictAsync(cfg, ct: ct);
            }
            catch (FdroidUpdateException ex)
            {
                Log.FdroidUpdateFailedAfterRebuild(
                    logs.CreateLogger(nameof(ReleasesEndpoints)),
                    ex, packageName, versionCode, duplicatesRemoved);
                var detail = duplicatesRemoved > 0
                    ? $" (removed {duplicatesRemoved} duplicate APKs before rebuild)"
                    : "";
                return Results.Json(
                    new DeleteReleaseFailureResponse($"fdroid update failed{detail}: {ex.Message}", duplicatesRemoved),
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.NoContent();
        }).RequireAuthorization(Scopes.ReleasesWrite)
            .Produces<DeleteReleaseFailureResponse>(StatusCodes.Status502BadGateway);
    }
}
