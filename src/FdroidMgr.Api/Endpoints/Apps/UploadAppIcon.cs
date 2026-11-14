using Microsoft.AspNetCore.Http.Features;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapUploadAppIcon(IEndpointRouteBuilder g)
    {
        g.MapPost("/{packageName}/icon", async (string packageName, HttpRequest request, IFdroidRepository db,
            IFdroidRepoService repo, IRepoIndexService index, HttpContext http, IOService io,
            IFdroidMetadataService metadata, CancellationToken ct) =>
        {
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = 10_000_000;
            }
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorResponse("icon file required"));
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg"))
            {
                return Results.BadRequest(new ErrorResponse("icon must be a PNG or JPEG"));
            }

            if (!string.Equals(packageName, Path.GetFileName(packageName), StringComparison.Ordinal))
            {
                return Results.BadRequest(new ErrorResponse("invalid package name"));
            }

            var cfg = await Cfg(db, ct);
            var repoRoot = metadata.ResolveRepoRoot(cfg);

            var yml = metadata.GetPath(packageName, repoRoot);
            var exists = yml is not null && io.FileExists(yml);
            if (!exists)
            {
                var index0 = await repo.ReadIndexAsync(cfg, ct);
                exists = index0?.Apps.Any(a => a.PackageName == packageName) == true;
            }

            if (!exists)
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var enUsDir = Path.Combine(repoRoot, "metadata", packageName, "en-US");
            io.CreateDirectory(enUsDir);
            var dest = Path.Combine(enUsDir, "icon.png");
            await using (var fs = io.CreateFile(dest))
            {
                await file.CopyToAsync(fs, ct);
            }

            await index.GenerateAsync(cfg, ct: ct);

            return Results.Ok(new AppIconResponse(packageName, $"{packageName}/en-US/icon.png"));
        }).RequireAuthorization(Scopes.AppsWrite)
            .DisableAntiforgery()
            .Produces<AppIconResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
