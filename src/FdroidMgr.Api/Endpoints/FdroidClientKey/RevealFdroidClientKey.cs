using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.FdroidClientKey;

public static partial class FdroidClientKeyEndpoints
{
    private static void MapRevealFdroidClientKey(IEndpointRouteBuilder g)
    {
        g.MapGet("/secret", async (IFdroidClientKeyService keys, IApiKeyService apiKeys, IFdroidRepository db, HttpContext http, ICurrentUserService currentUser, CancellationToken ct) =>
        {
            var cfg = await db.FindRepoConfigAsync(ct);
            var (key, secret) = await keys.RevealAsync(ct);
            return Results.Ok(new ClientKeySecretResponse(
                secret,
                key.KeyPrefix,
                cfg?.RepoUrl ?? "",
                apiKeys.BuildBasicAuthUrl(cfg?.RepoUrl ?? "", currentUser.GetUsername(http.User) is { Length: > 0 } un ? un : "fdroid", secret),
                key.LastRevealedUtc));
        }).Produces<ClientKeySecretResponse>();
    }
}
