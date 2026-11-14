using FdroidMgr.Api.Services;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Keys;

public static partial class KeysEndpoints
{
    private static void MapGenerateKey(IEndpointRouteBuilder g)
    {
        g.MapPost("", async (string packageName, string? branch, IFdroidRepository db, IKeyService keys,
            IFdroidMetadataService metadata, IFdroidRepoService repo, CancellationToken ct) =>
        {
            if (!await metadata.ExistsAsync(packageName, ct))
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var cfg = await db.GetRepoConfigAsync(ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var a = index?.Apps.FirstOrDefault(x => x.PackageName == packageName);
            if (a is not null && a.Versions.Count > 0)
            {
                return Results.Conflict(new ErrorResponse("app already has releases — use /keys/import instead"));
            }

            var key = await keys.GenerateKeyAsync(packageName, branch, ct);
            return Results.Ok(new AppKeyResponse(
                key.Id, key.PackageName, key.Alias, key.Branch, key.FingerprintSha256, key.StorePath, key.CreatedUtc));
        }).RequireAuthorization(Scopes.AppsWrite)
            .Produces<AppKeyResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }
}
