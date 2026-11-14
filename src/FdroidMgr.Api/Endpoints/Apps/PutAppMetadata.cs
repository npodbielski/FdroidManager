using FdroidMgr.Api.Services;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapPutAppMetadata(IEndpointRouteBuilder g)
    {
        g.MapPost("/{packageName}/metadata", async (string packageName, IFdroidRepository db, IFdroidRepoService repo,
            IFdroidMetadataService metadata, IRepoIndexService index, CancellationToken ct) =>
        {
            var cfg = await Cfg(db, ct);
            var index0 = await repo.ReadIndexAsync(cfg, ct);
            var fields = await ResolveAppFieldsAsync(packageName, index0, metadata, ct);
            if (fields is null)
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var path = await metadata.WriteAsync(packageName, fields, ct);
            await index.GenerateAsync(cfg, ct: ct);

            var fingerprints = await db.ListKeyFingerprintsAsync(packageName, ct);

            return Results.Ok(new AppMetadataWrittenResponse(
                packageName,
                path,
                fields.Name,
                fields.License,
                [.. fingerprints.Select(f => f.ToLowerInvariant())],
                true));
        }).RequireAuthorization(Scopes.AppsWrite)
            .Produces<AppMetadataWrittenResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
