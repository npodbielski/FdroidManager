using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.Keys;

public static partial class KeysEndpoints
{
    private static void MapListAppKeys(IEndpointRouteBuilder g)
    {
        g.MapGet("", async (string packageName, IFdroidRepository db, CancellationToken ct) =>
        {
            var appKeys = await db.ListKeysAsync(packageName, ct);
            return Results.Ok(appKeys.Select(k => new AppKeyResponse(
                k.Id,
                k.PackageName,
                k.Alias,
                k.Branch,
                k.FingerprintSha256,
                k.StorePath,
                k.CreatedUtc)));
        }).Produces<IEnumerable<AppKeyResponse>>();
    }
}
