using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    private static void MapInitRepo(IEndpointRouteBuilder g)
    {
        g.MapPost("/init", async (RepoInitRequest req, IRepoInitService init, CancellationToken ct) =>
        {
            var result = await init.InitAsync(req, ct);
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        }).RequireAuthorization("InteractiveOnly")
            .Produces<RepoInitResult>(StatusCodes.Status201Created);
    }
}
