using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapGetApp(IEndpointRouteBuilder g)
    {
        g.MapGet("/{packageName}", async (string packageName, IFdroidRepository db, IFdroidRepoService repo, CancellationToken ct) =>
        {
            var cfg = await Cfg(db, ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var app = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
            if (app is null)
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var appKeys = await db.ListKeysAsync(packageName, ct);

            var v2Icons = await repo.ReadV2IconsAsync(cfg, ct);
            var icon = ResolveIcon(app.Icon, v2Icons.TryGetValue(packageName, out var v2) ? v2 : null);

            return Results.Ok(new AppDetailResponse(
                app.PackageName,
                app.Name,
                app.Summary,
                app.Description,
                app.License,
                app.Web,
                app.Source,
                app.Tracker,
                icon,
                FdroidRepoService.PreferredScreenshots(app.Screenshots),
                [.. appKeys.Select(k => new AppKeyResponse(
                    k.Id,
                    k.PackageName,
                    k.Alias,
                    k.Branch,
                    k.FingerprintSha256,
                    k.StorePath,
                    k.CreatedUtc))],
                [.. app.Versions.OrderByDescending(v => v.VersionCode).Select(v => new AppVersionResponse(
                    v.VersionCode,
                    v.VersionName,
                    v.ApkName,
                    v.Sha256,
                    v.SizeBytes,
                    v.SignerSha256,
                    v.MinSdk,
                    v.TargetSdk,
                    v.Permissions))]));
        }).Produces<AppDetailResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
