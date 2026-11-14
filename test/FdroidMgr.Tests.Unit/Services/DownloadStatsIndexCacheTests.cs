using FdroidMgr.Api.Services;
using FluentAssertions;

namespace FdroidMgr.Tests.Unit.Services;

public class DownloadStatsIndexCacheTests
{
    [Fact]
    public void Get_EmptyCache_ReturnsNull()
    {
        var cache = new DownloadStatsIndexCache();
        cache.Get("/repo/index-v1.json", T(0)).Should().BeNull();
    }

    [Fact]
    public void Set_ThenGet_SamePathAndMtime_ReturnsMap()
    {
        var cache = new DownloadStatsIndexCache();
        cache.Set("/repo/index-v1.json", T(0), Map);
        cache.Get("/repo/index-v1.json", T(0)).Should().BeSameAs(Map);
    }

    [Fact]
    public void Get_DifferentPath_OrMtime_ReturnsNull()
    {
        var cache = new DownloadStatsIndexCache();
        cache.Set("/repo/index-v1.json", T(0), Map);

        cache.Get("/other/index-v1.json", T(0)).Should().BeNull();
        cache.Get("/repo/index-v1.json", T(1)).Should().BeNull();
    }

    [Fact]
    public void Set_OlderSnapshot_DoesNotRegressOverNewerOne()
    {
        var cache = new DownloadStatsIndexCache();
        cache.Set("/repo/index-v1.json", T(10), Map);

        cache.Set("/repo/index-v1.json", T(5), new Dictionary<string, (string, int)>(StringComparer.Ordinal));

        cache.Get("/repo/index-v1.json", T(10)).Should().BeSameAs(Map);
    }

    private static readonly IReadOnlyDictionary<string, (string Package, int VersionCode)> Map =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["com.example_1.apk"] = ("com.example", 1),
        };

    private static DateTime T(int minute) => new(2026, 8, 28, 12, minute, 0, DateTimeKind.Utc);
}
