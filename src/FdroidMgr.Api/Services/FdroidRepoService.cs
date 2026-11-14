
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class FdroidRepoService(ILogger<FdroidRepoService> log, IOService io, IRepoIndexSnapshot snapshot)
    : IFdroidRepoService
{
    private const string ChangelogLocale = "en-US";

    private static readonly string[] ScreenshotDirs =
        ["phoneScreenshots", "sevenInchScreenshots", "tenInchScreenshots", "tvScreenshots", "wearScreenshots"];

    public static int LastVersionCode(FdroidIndex? index, string packageName)
    {
        var app = index?.Apps.FirstOrDefault(a => a.PackageName == packageName);
        return app is null ? 0 : app.Versions.Select(v => v.VersionCode).DefaultIfEmpty(0).Max();
    }

    public static FdroidVersion? LastVersion(FdroidIndex? index, string packageName)
        => index?.Apps.FirstOrDefault(a => a.PackageName == packageName)?
            .Versions.OrderByDescending(v => v.VersionCode).FirstOrDefault();

    public static string? NextVersionName(string? versionName)
    {
        if (string.IsNullOrWhiteSpace(versionName))
        {
            return null;
        }

        var core = versionName.Trim();
        var plus = core.IndexOf('+');
        if (plus >= 0)
        {
            core = core[..plus];
        }

        core = core.Trim().TrimEnd('.');
        if (core.Length == 0)
        {
            return null;
        }

        var parts = core.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0].Trim(), out _) || !int.TryParse(parts[1].Trim(), out _))
        {
            return null;
        }

        var patch = 1;
        if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out var current))
        {
            patch = current + 1;
        }

        return $"{parts[0].Trim()}.{parts[1].Trim()}.{patch}";
    }

    public static (string VersionName, int VersionCode) NextVersion(FdroidIndex? index, string packageName)
    {
        var last = LastVersion(index, packageName);
        return (NextVersionName(last?.VersionName) ?? "1.0.0", last is null ? 1 : last.VersionCode + 1);
    }

    public static string ChangelogPath(string repoRoot, string packageName, int versionCode)
        => Path.Combine(repoRoot, "metadata", packageName, ChangelogLocale, "changelogs", versionCode + ".txt");

    public static string IndexPath(string repoRoot)
        => Path.Combine(repoRoot, "repo", "index-v1.json");
}
