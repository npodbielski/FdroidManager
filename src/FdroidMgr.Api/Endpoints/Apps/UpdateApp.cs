using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapUpdateApp(IEndpointRouteBuilder g)
    {
        g.MapPut("/{packageName}", async (string packageName, UpdateAppRequest req, IFdroidRepository db,
            IFdroidRepoService repo, IFdroidMetadataService metadata, IRepoIndexService index, CancellationToken ct) =>
        {
            var cfg = await Cfg(db, ct);
            var index0 = await repo.ReadIndexAsync(cfg, ct);
            var current = await ResolveAppFieldsAsync(packageName, index0, metadata, ct);
            if (current is null)
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var fields = new AppMetadataFields
            {
                Name = req.Name ?? current.Name,
                Summary = req.Summary ?? current.Summary,
                Description = req.Description ?? current.Description,
                License = req.License ?? current.License,
                Website = req.Website ?? current.Website,
                SourceCode = req.SourceCode ?? current.SourceCode,
                IssueTracker = req.IssueTracker ?? current.IssueTracker,
            };
            if (string.IsNullOrWhiteSpace(fields.Name))
            {
                return Results.BadRequest(new ErrorResponse("name is required"));
            }

            await metadata.WriteAsync(packageName, fields, ct);
            await index.GenerateAsync(cfg, ct: ct);
            return Results.Ok(new AppUpdatedResponse(packageName, fields.Name, fields.License));
        }).RequireAuthorization(Scopes.AppsWrite)
            .Produces<AppUpdatedResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
