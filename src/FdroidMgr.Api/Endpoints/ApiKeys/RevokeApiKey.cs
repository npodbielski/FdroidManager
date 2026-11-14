using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.ApiKeys;

public static class RevokeApiKey
{
    public static void Map(IEndpointRouteBuilder g)
    {
        g.MapDelete("/{id:int}", async (int id, IApiKeyService keys, HttpContext http,
            ICurrentUserService currentUser, CancellationToken ct)
            => await keys.RevokeAsync(id, currentUser.GetSubject(http.User), ct)
                ? Results.NoContent()
                : Results.NotFound(new ErrorResponse("api key not found")))
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
