using System.Text.Json;
using System.Text.Json.Serialization;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Services;

public partial class FdroidRepoService
{
    private static readonly JsonSerializerOptions SchemaOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public virtual async Task<FdroidIndex?> ReadIndexAsync(RepoConfig cfg, CancellationToken ct = default)
    {
        var repoRoot = RepoIndexService.ResolveRepoRoot(cfg);
        var path = IndexPath(repoRoot);
        var mtime = io.FileExists(path) ? io.GetLastWriteTimeUtc(path) : default;
        if (snapshot.TryGetIndex(path, mtime, out var cached))
        {
            return cached;
        }

        var index = await ReadIndexFileAsync(path, ct);
        snapshot.SetIndex(path, mtime, index);
        return index;
    }

    private async Task<FdroidIndex?> ReadIndexFileAsync(string path, CancellationToken ct)
    {
        if (!io.FileExists(path))
        {
            Log.IndexNotFound(log, path);
            return null;
        }

        try
        {
            await using var fs = io.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
            return Parse(doc.RootElement);
        }
        catch (JsonException ex)
        {
            Log.JsonParseFailed(log, ex, path);
            return null;
        }
    }

    public virtual async Task<IReadOnlyDictionary<string, string>> ReadV2IconsAsync(RepoConfig cfg, CancellationToken ct = default)
    {
        var repoRoot = RepoIndexService.ResolveRepoRoot(cfg);
        var path = Path.Combine(repoRoot, "repo", "index-v2.json");
        var mtime = io.FileExists(path) ? io.GetLastWriteTimeUtc(path) : default;
        if (snapshot.TryGetV2Icons(path, mtime, out var cached))
        {
            return cached!;
        }

        var icons = await ReadV2IconsFileAsync(path, ct);
        snapshot.SetV2Icons(path, mtime, icons);
        return icons;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadV2IconsFileAsync(string path, CancellationToken ct)
    {
        if (!io.FileExists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            await using var fs = io.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<IndexV2File>(fs, SchemaOptions, ct);
            return MapIcons(file);
        }
        catch (JsonException ex)
        {
            Log.JsonParseFailed(log, ex, path);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public static IReadOnlyDictionary<string, string> ParseV2Icons(JsonElement root)
        => MapIcons(root.Deserialize<IndexV2File>(SchemaOptions));

    private static IReadOnlyDictionary<string, string> MapIcons(IndexV2File? file)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (file?.Packages is null)
        {
            return result;
        }

        foreach (var (package, entry) in file.Packages)
        {
            var name = PickIconName(entry?.Metadata?.Icon) ?? PickIconName(entry?.Icon);
            if (!string.IsNullOrEmpty(name))
            {
                result[package] = name.TrimStart('/');
            }
        }

        return result;
    }

    private static string? PickIconName(Dictionary<string, IndexV2Icon>? icons)
    {
        if (icons is null)
        {
            return null;
        }

        foreach (var locale in new[] { "en-US", "en" })
        {
            if (icons.TryGetValue(locale, out var preferred) && !string.IsNullOrEmpty(preferred.Name))
            {
                return preferred.Name;
            }
        }

        foreach (var icon in icons.Values)
        {
            if (!string.IsNullOrEmpty(icon.Name))
            {
                return icon.Name;
            }
        }

        return null;
    }

    public static FdroidIndex? Parse(JsonElement root)
    {
        IndexFile? file;
        try
        {
            file = root.Deserialize<IndexFile>(SchemaOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (file is null || file.Apps.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            return null;
        }

        var apps = new List<FdroidApp>();
        if (file.Apps.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in file.Apps.EnumerateArray())
            {
                if (MapApp(TryRead<IndexApp>(entry), packageNameOverride: null) is { } app)
                {
                    apps.Add(app);
                }
            }
        }
        else
        {
            foreach (var prop in file.Apps.EnumerateObject())
            {
                if (MapApp(TryRead<IndexApp>(prop.Value), prop.Name) is { } app)
                {
                    apps.Add(app);
                }
            }
        }

        IReadOnlyList<FdroidApp> result = apps;
        if (file.Packages is not null)
        {
            var byName = apps.ToDictionary(a => a.PackageName, StringComparer.Ordinal);
            foreach (var (package, versions) in file.Packages)
            {
                if (!byName.TryGetValue(package, out var app))
                {
                    continue;
                }

                var mapped = versions
                    .Select(MapVersion)
                    .Where(v => v is not null)
                    .Select(v => v!)
                    .OrderByDescending(x => x.VersionCode)
                    .ToArray();
                if (mapped.Length > 0)
                {
                    byName[package] = app with { Versions = mapped };
                }
            }

            result = [.. byName.Values];
        }

        var repoName = file.Repo?.Name ?? "";
        var repoUrl = file.Repo?.Address ?? file.Repo?.Url;
        var timestamp = file.Repo?.Timestamp ?? 0;
        return new FdroidIndex(repoName, repoUrl, timestamp, [.. result.OrderBy(a => a.PackageName)]);
    }

    private static T? TryRead<T>(JsonElement element) where T : class
    {
        try
        {
            return element.ValueKind == JsonValueKind.Object ? element.Deserialize<T>(SchemaOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FdroidApp? MapApp(IndexApp? entry, string? packageNameOverride)
    {
        if (entry is null)
        {
            return null;
        }

        var packageName = packageNameOverride ?? entry.PackageName ?? entry.Id;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        var versions = new List<FdroidVersion>();
        if (entry.Versions is not null)
        {
            foreach (var legacy in entry.Versions.Values)
            {
                if (MapVersionLegacy(legacy) is { } ver)
                {
                    versions.Add(ver);
                }
            }
        }

        return new FdroidApp(
            packageName,
            entry.Name ?? "",
            entry.Summary ?? "",
            entry.Description ?? "",
            entry.License ?? "Unknown",
            entry.WebSite ?? entry.Web,
            entry.SourceCode ?? entry.Source,
            entry.IssueTracker ?? entry.Tracker,
            entry.Icon,
            MapScreenshots(entry.Localized, packageName),
            [.. versions.OrderByDescending(v => v.VersionCode)]);
    }

    private static IReadOnlyDictionary<string, string[]>? MapScreenshots(
        Dictionary<string, Dictionary<string, JsonElement>>? localized, string packageName)
    {
        if (localized is null)
        {
            return null;
        }

        var byLocale = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (locale, fields) in localized)
        {
            var paths = new List<string>();
            foreach (var dir in ScreenshotDirs)
            {
                if (!fields.TryGetValue(dir, out var arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var name in arr.EnumerateArray()
                             .Where(e => e.ValueKind == JsonValueKind.String)
                             .Select(e => e.GetString()))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        paths.Add($"{packageName}/{locale}/{dir}/{name}");
                    }
                }
            }

            if (paths.Count > 0)
            {
                byLocale[locale] = paths;
            }
        }

        return byLocale.Count == 0
            ? null
            : byLocale.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> PreferredScreenshots(IReadOnlyDictionary<string, string[]>? screenshots)
    {
        if (screenshots is null || screenshots.Count == 0)
        {
            return [];
        }

        foreach (var locale in new[] { "en-US", "en" })
        {
            if (screenshots.TryGetValue(locale, out var list) && list.Length > 0)
            {
                return list;
            }
        }

        foreach (var kv in screenshots)
        {
            if (kv.Value.Length > 0)
            {
                return kv.Value;
            }
        }

        return [];
    }

    private static FdroidVersion? MapVersion(IndexVersion? v)
    {
        if (v is null || v.VersionCode <= 0)
        {
            return null;
        }

        return new FdroidVersion(
            v.VersionCode,
            v.VersionName ?? "",
            StripLeadingSlash(v.ApkName ?? ""),
            v.Hash ?? "",
            v.Size,
            SignerSha256(v.Signer),
            v.MinSdkVersion,
            v.TargetSdkVersion,
            Permissions(v.UsesPermission ?? v.UsesPermissionCamel));
    }

    private static FdroidVersion? MapVersionLegacy(LegacyVersion? v)
    {
        if (v is null)
        {
            return null;
        }

        var apkName = StripLeadingSlash(v.File?.Name ?? "");
        var size = v.File?.Size ?? 0;
        var sha256 = v.File?.Hash?.Sha256 ?? "";
        var signer = v.Manifest?.Signer?.Sha256?.FirstOrDefault(s => !string.IsNullOrEmpty(s));
        var minSdk = v.Manifest?.UsesSdk?.MinSdkVersion;
        var targetSdk = v.Manifest?.UsesSdk?.TargetSdkVersion;
        var permissions = Permissions(v.Manifest?.UsesPermission ?? v.Manifest?.UsesPermissionCamel);

        return new FdroidVersion(v.VersionCode, v.VersionName ?? "", apkName, sha256, size, signer, minSdk, targetSdk, permissions);
    }

    private static string? SignerSha256(JsonElement signer)
    {
        if (signer.ValueKind == JsonValueKind.String)
        {
            return signer.GetString();
        }

        if (signer.ValueKind == JsonValueKind.Array)
        {
            return signer.EnumerateArray()
                .Select(e => e.GetString())
                .FirstOrDefault(s => !string.IsNullOrEmpty(s));
        }

        return null;
    }

    private static string[] Permissions(List<JsonElement>? items)
    {
        if (items is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    result.Add(s);
                }
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                var first = item.EnumerateArray()
                    .FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
                if (first.ValueKind == JsonValueKind.String && first.GetString() is { Length: > 0 } inner)
                {
                    result.Add(inner);
                }
            }
        }

        return [.. result.Distinct(StringComparer.Ordinal)];
    }

    private static string StripLeadingSlash(string name) => name.StartsWith('/') ? name[1..] : name;

    private static partial class Log
    {
        [LoggerMessage(EventId = 31, Level = LogLevel.Debug,
            Message = "index-v1.json not found at {Path}")]
        public static partial void IndexNotFound(ILogger logger, string path);

        [LoggerMessage(EventId = 32, Level = LogLevel.Warning,
            Message = "failed to parse {Path}")]
        public static partial void JsonParseFailed(ILogger logger, Exception ex, string path);

        [LoggerMessage(EventId = 33, Level = LogLevel.Warning,
            Message = "failed to chmod changelog file {Path}")]
        public static partial void ChangelogChmodFailed(ILogger logger, Exception ex, string path);
    }
}
