using Microsoft.AspNetCore.Http.Features;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Keys;

public static partial class KeysEndpoints
{
    private static void MapImportKey(IEndpointRouteBuilder g)
    {
        g.MapPost("/import", async (string packageName, string? branch, HttpRequest request,
            IFdroidRepository db, IKeyService keys, IFdroidMetadataService metadata, IFdroidRepoService repo,
            HttpContext http, IOService io, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = 20_000_000;
            }
            var form = await request.ReadFormAsync(ct);
            var keystore = form.Files.FirstOrDefault(f => f.Name == "keystore");
            if (keystore is null || keystore.Length == 0)
            {
                return Results.BadRequest(new ErrorResponse("keystore file required (form field 'keystore')"));
            }

            var storePassword = form["storePassword"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(storePassword))
            {
                return Results.BadRequest(new ErrorResponse("storePassword is required"));
            }

            var keyPassword = string.IsNullOrEmpty(form["keyPassword"].FirstOrDefault())
                ? storePassword
                : form["keyPassword"].FirstOrDefault()!;
            var alias = string.IsNullOrEmpty(form["alias"].FirstOrDefault())
                ? "release"
                : form["alias"].FirstOrDefault()!;

            if (!await metadata.ExistsAsync(packageName, ct))
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var cfg = await db.GetRepoConfigAsync(ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var recordedApp = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
            var recordedSigner = recordedApp?.Versions
                .Select(v => v.SignerSha256)
                .FirstOrDefault(s => !string.IsNullOrEmpty(s));

            using var buffer = new MemoryStream();
            await keystore.CopyToAsync(buffer, ct);

            if (recordedSigner is not null)
            {
                buffer.Position = 0;
                var fingerprint = KeyService.GetFingerprint(buffer, storePassword, alias, io);
                if (fingerprint == "unknown")
                {
                    return Results.BadRequest(new ErrorResponse("unreadable keystore (wrong password or alias)"));
                }

                if (!string.Equals(fingerprint, recordedSigner, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(new ErrorResponse($"imported key (fp {fingerprint}) does not match existing releases (signed with fp {recordedSigner})"));
                }
            }
            else if (recordedApp is not null && recordedApp.Versions.Count > 0)
            {
                Log.ImportWithoutSignerComparison(logs.CreateLogger(nameof(KeysEndpoints)), packageName);
            }

            buffer.Position = 0;
            var key = await keys.ImportKeyAsync(packageName, buffer, storePassword, keyPassword, alias, branch, ct);
            return Results.Ok(new AppKeyResponse(
                key.Id, key.PackageName, key.Alias, key.Branch, key.FingerprintSha256, key.StorePath, key.CreatedUtc));
        }).RequireAuthorization(Scopes.AppsWrite)
            .DisableAntiforgery()
            .Produces<AppKeyResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }
}
