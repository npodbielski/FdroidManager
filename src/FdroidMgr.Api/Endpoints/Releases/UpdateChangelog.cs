using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Releases;

public static partial class ReleasesEndpoints
{
    private static void MapUpdateChangelog(IEndpointRouteBuilder g)
    {
        g.MapPatch("/{versionCode:int}", async (string packageName, int versionCode, UpdateChangelogRequest req,
            IFdroidRepository db, IFdroidRepoService repo, IOService io, IFdroidMetadataService metadata, CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var app0 = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
            var version = app0?.Versions.FirstOrDefault(v => v.VersionCode == versionCode);
            if (app0 is null || version is null)
            {
                return Results.NotFound(new ErrorResponse("release not found"));
            }

            if (string.IsNullOrWhiteSpace(req.Changelog))
            {
                var path = FdroidRepoService.ChangelogPath(metadata.ResolveRepoRoot(cfg), packageName, versionCode);
                if (io.FileExists(path))
                {
                    io.DeleteFile(path);
                }
            }
            else
            {
                await repo.WriteChangelogAsync(cfg, packageName, versionCode, req.Changelog.Trim(), ct);
            }

            var fresh = await repo.ReadChangelogAsync(cfg, packageName, versionCode, ct);
            return Results.Ok(ToDto(packageName, version, () => fresh));
        }).RequireAuthorization(Scopes.ReleasesWrite)
            .Produces<ReleaseDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
