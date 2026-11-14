using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Releases;

public static partial class ReleasesEndpoints
{
    private static void MapNextReleaseVersion(IEndpointRouteBuilder g)
    {
        g.MapGet("/next-version", async (string packageName, IFdroidRepository db, IFdroidRepoService repo, IOService io, IFdroidMetadataService metadata, CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var app0 = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);

            if (app0 is null)
            {
                var metadataPath = metadata.GetPath(packageName, metadata.ResolveRepoRoot(cfg));
                if (metadataPath is null || !io.FileExists(metadataPath))
                {
                    return Results.NotFound(new ErrorResponse("app not found"));
                }
            }

            var last = FdroidRepoService.LastVersion(index, packageName);
            var (nextName, nextCode) = FdroidRepoService.NextVersion(index, packageName);

            return Results.Ok(new NextReleaseVersionDto
            {
                PackageName = packageName,
                PreviousVersionName = last?.VersionName,
                PreviousVersionCode = last?.VersionCode,
                VersionName = nextName,
                VersionCode = nextCode,
            });
        }).Produces<NextReleaseVersionDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
