using FdroidMgr.Api.Data;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class DownloadStatsServiceTests : IDisposable
{
    private readonly string _dir;

    public DownloadStatsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdroidmgr-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task RecordAsync_UpsertsSameDayBucket_InsteadOfRows()
    {
        var db = TestDb.Create();
        await TestDb.SeedRepoConfigAsync(db, Path.Combine(_dir, "repo"), Path.Combine(_dir, "keys"));
        var svc = Create(db);

        await svc.RecordAsync("com.example", 5);
        await svc.RecordAsync("com.example", 5);

        var row = await db.DownloadCount.SingleAsync();
        row.Count.Should().Be(2);
        row.PackageName.Should().Be("com.example");
        row.VersionCode.Should().Be(5);
        row.DayUtc.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public async Task RecordAsync_SkipsUnattributable_Downloads()
    {
        var db = TestDb.Create();
        await TestDb.SeedRepoConfigAsync(db, Path.Combine(_dir, "repo"), Path.Combine(_dir, "keys"));
        var svc = Create(db);

        await svc.RecordAsync(null, null);
        await svc.RecordAsync("", 5);
        await svc.RecordAsync("com.example", null);

        (await db.DownloadCount.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void FallbackRegex_ParsesCanonicalName_AndRejectsOthers()
    {
        DownloadStatsService.FallbackRegex("com.example.demo_42.apk")
            .Should().Be(("com.example.demo", 42));
        DownloadStatsService.FallbackRegex("no_version.apk").Should().Be((null, null));
        DownloadStatsService.FallbackRegex("index.xml").Should().Be((null, null));
    }

    [Fact]
    public async Task ResolveAsync_UsesIndexMap_WhenRegexWouldFail()
    {
        var db = TestDb.Create();
        WriteIndex(db, "weird_name.apk");
        var svc = Create(db);
        var cfg = await db.RepoConfig.FirstAsync();

        var (pkg, vc) = await svc.ResolveAsync("weird_name.apk", cfg);
        pkg.Should().Be("com.example");
        vc.Should().Be(5);
    }

    [Fact]
    public async Task ResolveAsync_SharesIndexMapCache_AcrossServiceInstances()
    {
        var db = TestDb.Create();
        WriteIndex(db);
        var cache = new DownloadStatsIndexCache();
        var svc1 = Create(db, cache);
        var svc2 = Create(db, cache);
        var cfg = await db.RepoConfig.FirstAsync();

        await svc1.ResolveAsync("com.example_5.apk", cfg);

        var indexPath = FdroidRepoService.IndexPath(RepoIndexService.ResolveRepoRoot(cfg));
        var mtime = File.GetLastWriteTimeUtc(indexPath);
        cache.Get(indexPath, mtime).Should().NotBeNull();

        var (pkg2, vc2) = await svc2.ResolveAsync("com.example_5.apk", cfg);
        pkg2.Should().Be("com.example");
        vc2.Should().Be(5);
    }

    [Fact]
    public async Task GetAsync_Aggregates_AndJoinsNames_FromIndex()
    {
        var db = TestDb.Create();
        WriteIndex(db);
        var svc = Create(db);
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.RecordAsync("com.example", 5);
        await svc.RecordAsync("com.example", 5);
        await svc.RecordAsync("com.example", 4);

        var dto = await svc.GetAsync(cfg);

        dto.TotalDownloads.Should().Be(3);
        dto.Apps.Should().ContainSingle();
        dto.Apps[0].PackageName.Should().Be("com.example");
        dto.Apps[0].Name.Should().Be("Example");
        dto.Apps[0].Downloads.Should().Be(3);
        dto.Apps[0].LatestVersionCode.Should().Be(5);

        dto.Releases.Should().HaveCount(2);
        dto.Releases.Should().Contain(r => r.VersionCode == 5 && r.Downloads == 2 && r.VersionName == "1.2.0");
        dto.Releases.Should().Contain(r => r.VersionCode == 4 && r.Downloads == 1);

        dto.Daily.Should().HaveCount(30);
        dto.Daily.Sum(d => d.Downloads).Should().Be(3);
        dto.Daily.Count(d => d.Downloads > 0).Should().Be(1);
    }

    private static DownloadStatsService Create(FdroidDb db, DownloadStatsIndexCache? cache = null)
        => new(db, new FdroidRepoService(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot()),
            cache ?? new DownloadStatsIndexCache(),
            NullLogger<DownloadStatsService>.Instance, TestIo.Real);

    private void WriteIndex(FdroidDb db, string apkName = "com.example_5.apk")
    {
        var repoPath = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(repoPath);
        File.WriteAllText(Path.Combine(repoPath, "index-v1.json"), $$"""
        {
          "apps": [ { "packageName": "com.example", "name": "Example", "summary": "", "license": "MIT" } ],
          "packages": {
            "com.example": [
              { "versionCode": 5, "versionName": "1.2.0", "apkName": "{{apkName}}", "hash": "abc", "size": 100 }
            ]
          }
        }
        """);

        TestDb.SeedRepoConfigAsync(db, repoPath, Path.Combine(_dir, "keys")).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
