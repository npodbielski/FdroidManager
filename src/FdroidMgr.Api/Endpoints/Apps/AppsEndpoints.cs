using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    public static void MapAppsApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/apps").RequireAuthorization(Scopes.Read);
        MapListApps(g);
        MapGetApp(g);
        MapUploadAppIcon(g);
        MapCreateApp(g);
        MapUpdateApp(g);
        MapDeleteApp(g);
        MapPutAppMetadata(g);
        MapGetAppMetadata(g);

    }

    private static Task<RepoConfig> Cfg(IFdroidRepository db, CancellationToken ct)
        => db.GetRepoConfigAsync(ct);

    private static AppListItem ToItem(FdroidApp a, string? icon) => new(
        a.PackageName,
        a.Name,
        a.Summary,
        a.License,
        icon,
        a.Versions.Count,
        a.Versions.OrderByDescending(v => v.VersionCode).Select(v => v.VersionName).FirstOrDefault(),
        a.Versions.Count > 0 ? a.Versions.Max(v => v.VersionCode) : null);

    private static string? ResolveIcon(string? v1Basename, string? v2Icon)
    {
        if (!string.IsNullOrWhiteSpace(v2Icon))
        {
            return v2Icon.TrimStart('/');
        }

        if (!string.IsNullOrWhiteSpace(v1Basename))
        {
            return "icons/" + v1Basename;
        }

        return null;
    }

    private static async Task<AppMetadataFields?> ResolveAppFieldsAsync(string packageName, FdroidIndex? index,
        IFdroidMetadataService metadata, CancellationToken ct)
    {
        var app = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
        if (app is not null)
        {
            return new AppMetadataFields
            {
                Name = app.Name,
                Summary = app.Summary,
                Description = app.Description,
                License = app.License,
                Website = app.Web,
                SourceCode = app.Source,
                IssueTracker = app.Tracker,
            };
        }

        var yaml = await metadata.ReadAsync(packageName, ct);
        return yaml is null ? null : FdroidMetadataService.ParseAppMetadata(yaml);
    }

    private static AppMetadataFields Fields(CreateAppRequest req) => new()
    {
        Name = req.Name,
        Summary = req.Summary,
        Description = req.Description,
        License = req.License,
        Website = req.Website,
        SourceCode = req.SourceCode,
        IssueTracker = req.IssueTracker,
    };

    private static partial class Log
    {
        [LoggerMessage(EventId = 36, Level = LogLevel.Warning,
            Message = "key generation failed for {Pkg}")]
        public static partial void KeyGenerationFailed(ILogger logger, Exception ex, string pkg);
    }
}
