using HamsterWheel.HLinq.Request;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.ApiKeys;

public static class GetApiKeys
{
    public static void Map(IEndpointRouteBuilder g)
    {
        g.MapGet("", async (HLinqQuery<ApiKeyListItem> query, IFdroidRepository db, HttpContext http,
            ICurrentUserService currentUser, CancellationToken ct) =>
        {
            var subject = currentUser.GetSubject(http.User);
            var user = await db.FindUserBySubjectAsync(subject, ct);
            if (user is null)
            {
                return Results.Ok(Array.Empty<ApiKeyListItem>());
            }

            var apiKeys = (await db.ListApiKeysAsync(user.Id, ct)).Select(k => new ApiKeyListItem
            {
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                Scopes = k.Scopes,
                CreatedUtc = k.CreatedUtc,
                LastUsedUtc = k.LastUsedUtc,
                ExpiresUtc = k.ExpiresUtc,
                RevokedUtc = k.RevokedUtc,
            }).AsQueryable();

            return Results.Ok(query.ApplyTo(apiKeys, ct));
        }).Produces<IEnumerable<ApiKeyListItem>>();
    }
}
