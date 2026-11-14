using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Endpoints.Releases;

public static partial class ReleasesEndpoints
{
    public static void MapReleasesApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/apps/{packageName}/releases").RequireAuthorization(Scopes.Read);
        MapListReleases(g);
        MapNextReleaseVersion(g);
        MapPublishRelease(g);
        MapDeleteRelease(g);
        MapUpdateChangelog(g);

    }

    private static Task<int> RemoveDuplicateApksAsync(RepoConfig cfg, string packageName, int versionCode, IOService io)
    {
        var repoDir = cfg.RepoPath;
        if (!io.DirectoryExists(repoDir))
        {
            return Task.FromResult(0);
        }

        var canonicalName = $"{packageName}_{versionCode}.apk";
        var canonicalPath = Path.Combine(repoDir, canonicalName);

        if (!io.FileExists(canonicalPath))
        {
            return Task.FromResult(0);
        }

        var duplicates = new List<string>();
        foreach (var file in io.EnumerateFiles(repoDir, "*.apk"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName == canonicalName)
            {
                continue;
            }

            duplicates.Add(file);
        }

        foreach (var dup in duplicates)
        {
            try
            {
                io.DeleteFile(dup);
            }
            catch (IOException)
            {
            }
        }

        return Task.FromResult(duplicates.Count);
    }

    private static ReleaseDto ToDto(string packageName, FdroidVersion v, Func<string?> changelogLoader)
        => new()
        {
            PackageName = packageName,
            VersionCode = v.VersionCode,
            VersionName = v.VersionName,
            ApkFileName = v.ApkName,
            Sha256 = v.Sha256,
            SizeBytes = v.SizeBytes,
            SignerSha256 = v.SignerSha256,
            MinSdk = v.MinSdk,
            TargetSdk = v.TargetSdk,
            Permissions = v.Permissions,
            ChangelogLoader = changelogLoader,
        };

    private static partial class Log
    {
        [LoggerMessage(EventId = 38, Level = LogLevel.Error,
            Message = "fdroid update failed for {PackageName} v{VersionCode} (removed {DuplicatesRemoved} duplicate APKs before rebuild)")]
        public static partial void FdroidUpdateFailedAfterRebuild(ILogger logger, Exception ex, string packageName, int versionCode, int duplicatesRemoved);
    }
}
