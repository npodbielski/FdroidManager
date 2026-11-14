using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.FdroidClientKey;

public static partial class FdroidClientKeyEndpoints
{
    private static void MapGetFdroidClientKey(IEndpointRouteBuilder g)
    {
        g.MapGet("", async (IFdroidClientKeyService keys, IApiKeyService apiKeys, IFdroidRepository db, HttpContext http, ICurrentUserService currentUser, CancellationToken ct) =>
        {
            var user = await apiKeys.GetOrCreateUserAsync(currentUser.GetSubject(http.User), currentUser.GetUsername(http.User), ct);
            var key = await keys.GetOrCreateAsync(user, ct);
            var cfg = await db.FindRepoConfigAsync(ct);
            var username = currentUser.GetUsername(http.User) is { Length: > 0 } un ? un : "fdroid";
            return Results.Ok(new ClientKeyStateResponse(
                key.Id,
                username,
                key.KeyPrefix,
                cfg?.RepoUrl ?? "",
                apiKeys.BuildBasicAuthUrl(cfg?.RepoUrl ?? "", username, key.KeyPrefix + "…"),
                key.CreatedUtc,
                key.LastUsedUtc,
                key.LastRevealedUtc,
                key.RegeneratedUtc));
        }).Produces<ClientKeyStateResponse>();
    }
}
