using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapGetAppMetadata(IEndpointRouteBuilder g)
    {
        g.MapGet("/{packageName}/metadata", async (string packageName, IFdroidRepository db,
            IFdroidMetadataService metadata, CancellationToken ct) =>
        {
            var yaml = await metadata.ReadAsync(packageName, ct);
            if (yaml is null)
            {
                return Results.NotFound(new ErrorResponse("metadata not written yet"));
            }

            var cfg = await Cfg(db, ct);
            var path = metadata.GetPath(packageName, metadata.ResolveRepoRoot(cfg)) ?? "";
            return Results.Ok(new AppMetadataResponse(path, yaml));
        }).Produces<AppMetadataResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
