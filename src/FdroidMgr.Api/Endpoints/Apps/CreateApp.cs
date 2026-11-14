using FdroidMgr.Api.Services;
using FluentValidation;
using FdroidMgr.Api.Validation;
using FdroidMgr.Api.Models;

using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapCreateApp(IEndpointRouteBuilder g)
    {
        g.MapPost("", async (CreateAppRequest req, IValidator<CreateAppRequest> validator,
            IKeyService keys, IFdroidMetadataService metadata, ILoggerFactory logs,
            CancellationToken ct) =>
        {
            if (validator.ValidateOrProblem(req) is { } problem)
            {
                return problem;
            }

            if (await metadata.ExistsAsync(req.PackageName, ct))
            {
                return Results.Conflict(new ErrorResponse($"app {req.PackageName} already exists"));
            }

            string? keyFingerprint = null;
            if (req.GenerateKey)
            {
                try
                {
                    var key = await keys.GenerateKeyAsync(req.PackageName, null, ct);
                    keyFingerprint = key.FingerprintSha256;
                }
                catch (Exception ex)
                {
                    Log.KeyGenerationFailed(logs.CreateLogger(nameof(AppsEndpoints)), ex, req.PackageName);
                }
            }

            var path = await metadata.WriteAsync(req.PackageName, Fields(req), ct);

            return Results.Created($"/api/apps/{req.PackageName}", new AppCreatedResponse(
                req.PackageName,
                path,
                req.Name,
                req.License,
                keyFingerprint is null
                    ? []
                    : [keyFingerprint.ToLowerInvariant()]));
        }).RequireAuthorization(Scopes.AppsWrite)
            .Produces<AppCreatedResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }
}
