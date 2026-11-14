using System.Data.Common;
using System.Globalization;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class DownloadStatsService(
    FdroidDb db,
    IFdroidRepoService repo,
    IDownloadStatsIndexCache cache,
    ILogger<DownloadStatsService> log,
    IOService io)
    : IDownloadStatsService
{

    public async Task<(string? Package, int? VersionCode)> ResolveAsync(
        string apkFileName, RepoConfig cfg, CancellationToken ct = default)
    {
        var map = await GetMapAsync(cfg, ct);
        if (map.TryGetValue(apkFileName, out var hit))
        {
            return hit;
        }

        return FallbackRegex(apkFileName);
    }

    public static (string? Package, int? VersionCode) FallbackRegex(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        name = name[..^4];
        var sep = name.LastIndexOf('_');
        if (sep > 0
            && int.TryParse(name[(sep + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var vc)
            && vc > 0)
        {
            return (name[..sep], vc);
        }

        return (null, null);
    }

    public async Task RecordAsync(string? package, int? versionCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(package) || versionCode is null)
        {
            return;
        }

        try
        {
            var day = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "DownloadCount" ("DayUtc","PackageName","VersionCode","Count") VALUES ({0},{1},{2},1) ON CONFLICT("DayUtc","PackageName","VersionCode") DO UPDATE SET "Count" = "Count" + 1""",
                [day, package, versionCode.Value],
                ct);
        }
        catch (DbException ex)
        {
            Log.DownloadRecordFailed(log, ex, package, versionCode.Value);
        }
    }

    public async Task<DownloadStatsDto> GetAsync(RepoConfig cfg, CancellationToken ct = default)
    {
        var rows = await db.DownloadCount.AsNoTracking().ToListAsync(ct);
        var index = await repo.ReadIndexAsync(cfg, ct);

        var total = rows.Sum(r => (long)r.Count);
        var apps = rows
            .Where(r => !string.IsNullOrEmpty(r.PackageName))
            .GroupBy(r => r.PackageName!)
            .Select(g =>
            {
                var lastVc = FdroidRepoService.LastVersionCode(index, g.Key);
                return new AppDownloadStat
                {
                    PackageName = g.Key,
                    Name = index?.Apps.FirstOrDefault(a => a.PackageName == g.Key)?.Name,
                    Downloads = g.Sum(r => r.Count),
                    LatestVersionCode = lastVc > 0 ? lastVc : null,
                };
            })
            .OrderByDescending(a => a.Downloads)
            .ThenBy(a => a.PackageName, StringComparer.Ordinal)
            .ToList();

        var releases = rows
            .Where(r => !string.IsNullOrEmpty(r.PackageName) && r.VersionCode.HasValue)
            .GroupBy(r => (Package: r.PackageName!, VersionCode: r.VersionCode!.Value))
            .Select(g => new ReleaseDownloadStat
            {
                PackageName = g.Key.Package,
                VersionCode = g.Key.VersionCode,
                VersionName = index?.Apps.FirstOrDefault(a => a.PackageName == g.Key.Package)
                    ?.Versions.FirstOrDefault(v => v.VersionCode == g.Key.VersionCode)?.VersionName,
                Downloads = g.Sum(r => (long)r.Count),
            })
            .OrderByDescending(a => a.Downloads)
            .ThenBy(a => a.PackageName, StringComparer.Ordinal)
            .ThenBy(a => a.VersionCode)
            .ToList();

        var dailySums = rows
            .GroupBy(r => r.DayUtc)
            .ToDictionary(g => g.Key, g => g.Sum(r => (long)r.Count), StringComparer.Ordinal);

        var today = DateTime.UtcNow.Date;
        var daily = new List<DailyDownloadStat>(30);
        for (var d = today.AddDays(-29); d <= today; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            daily.Add(new DailyDownloadStat
            {
                Day = key,
                Downloads = dailySums.TryGetValue(key, out var c) ? c : 0,
            });
        }

        return new DownloadStatsDto
        {
            TotalDownloads = total,
            Apps = apps,
            Releases = releases,
            Daily = daily,
        };
    }

    private async Task<IReadOnlyDictionary<string, (string Package, int VersionCode)>> GetMapAsync(
        RepoConfig cfg, CancellationToken ct)
    {
        var path = FdroidRepoService.IndexPath(RepoIndexService.ResolveRepoRoot(cfg));
        var mtime = io.FileExists(path) ? io.GetLastWriteTimeUtc(path) : default;

        var cached = cache.Get(path, mtime);
        if (cached is not null)
        {
            return cached;
        }

        var map = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        var index = await repo.ReadIndexAsync(cfg, ct);
        if (index is not null)
        {
            foreach (var app in index.Apps)
            {
                foreach (var v in app.Versions)
                {
                    if (!string.IsNullOrEmpty(v.ApkName))
                    {
                        map[v.ApkName] = (app.PackageName, v.VersionCode);
                    }
                }
            }
        }

        cache.Set(path, mtime, map);
        return map;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 20, Level = LogLevel.Warning,
            Message = "failed to record download for {Package} v{Version}")]
        public static partial void DownloadRecordFailed(ILogger logger, Exception ex, string package, int version);
    }
}
