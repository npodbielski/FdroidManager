using Microsoft.AspNetCore.Http.Features;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Releases;

public static partial class ReleasesEndpoints
{
    private static void MapPublishRelease(IEndpointRouteBuilder g)
    {
        g.MapPost("", async (string packageName, HttpRequest request, IReleaseService publisher, HttpContext http,
            CancellationToken ct) =>
        {
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = 200_000_000;
            }
            var form = await request.ReadFormAsync(ct);
            var apk = form.Files.FirstOrDefault(f => f.Name == "apk");
            if (apk is null || apk.Length == 0)
            {
                return Results.BadRequest(new ErrorResponse("APK file required (form field 'apk')"));
            }

            var changelog = form["changelog"].FirstOrDefault();

            var result = await publisher.PublishAsync(packageName, apk, changelog, branch: null, ct);
            return Results.Ok(result);
        }).RequireAuthorization(Scopes.ReleasesWrite)
            .DisableAntiforgery()
            .Produces<ReleaseDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);
    }
}
