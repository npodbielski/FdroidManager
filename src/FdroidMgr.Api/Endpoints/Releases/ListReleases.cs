using FdroidMgr.Api.Models;
using HamsterWheel.HLinq.Request;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Releases;

public static partial class ReleasesEndpoints
{
    private static void MapListReleases(IEndpointRouteBuilder g)
    {
        g.MapGet("", async (string packageName, HLinqQuery<ReleaseDto> query, IFdroidRepository db,
            IFdroidRepoService repo, IOService io, IFdroidMetadataService metadata, CancellationToken ct) =>
        {
            var cfg = await db.GetRepoConfigAsync(ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var app0 = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
            if (app0 is null)
            {
                var metadataPath = metadata.GetPath(packageName, metadata.ResolveRepoRoot(cfg));
                if (metadataPath is not null && io.FileExists(metadataPath))
                {
                    return Results.Ok(new List<ReleaseDto>());
                }

                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var rows = app0.Versions
                .OrderByDescending(v => v.VersionCode)
                .Select(v => ToDto(packageName, v, () => repo.ReadChangelog(cfg, packageName, v.VersionCode)))
                .AsQueryable();
            return Results.Ok(query.ApplyTo(rows, ct));
        }).Produces<IEnumerable<ReleaseDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
