using System.Net;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Releases;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class ReleasesHlinqApiTests
{
    private const string Pkg = "org.example.app";

    [Fact]
    public async Task Default_ReturnsEverythingNewestFirst_WithChangelogs()
    {
        using var host = Boot();
        var body = await GetAsync(host.Client("admin"), ReleasesUrl());

        Codes(body).Should().Equal(4, 3, 2, 1);

        foreach (var r in body.EnumerateArray())
        {
            var code = r.GetProperty("versionCode").GetInt32();
            var expected = Versions.First(v => v.Code == code);
            var changelog = r.GetProperty("changelog");
            if (expected.Changelog)
            {
                changelog.GetString().Should().Be($"changelog for {expected.Name}");
            }
            else
            {
                changelog.ValueKind.Should().Be(JsonValueKind.Null);
            }
        }
    }

    [Fact]
    public async Task Take_PaysChangelogReadsOnlyForSurvivingRows()
    {
        using var host = Boot();
        var c = host.Client("admin");

        var body = await GetAsync(c, ReleasesUrl("?orderByDescending[x.versionCode].skip[0].take[3]"));

        Codes(body).Should().Equal(4, 3, 2);

        body.EnumerateArray().Single(r => r.GetProperty("versionCode").GetInt32() == 3)
            .GetProperty("changelog").GetString().Should().Be("changelog for 2.0.0");
    }

    [Fact]
    public async Task Take_WithoutOrderBy_KeepsTheDefaultNewestFirst()
    {
        using var host = Boot();
        var body = await GetAsync(host.Client("admin"), ReleasesUrl("?take[2]"));

        Codes(body).Should().Equal(4, 3);
    }

    [Fact]
    public async Task OrderBy_Ascending_ReturnsOldestFirst()
    {
        using var host = Boot();
        var body = await GetAsync(host.Client("admin"), ReleasesUrl("?orderBy[x.versionCode]"));

        Codes(body).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Where_VersionNameContains_CaseInsensitive_MatchesTheUiQuery()
    {
        using var host = Boot();
        var c = host.Client("admin");

        string Ui(string term) => $"?where[x.versionName.contains({term}, StringComparison.InvariantCultureIgnoreCase)]";

        Codes(await GetAsync(c, ReleasesUrl(Ui("2.")))).Should().Equal(4, 3);
        Codes(await GetAsync(c, ReleasesUrl(Ui("1.0.")))).Should().Equal(1);
        Codes(await GetAsync(c, ReleasesUrl(Ui("nope")))).Should().BeEmpty();
    }

    [Fact]
    public async Task Where_And_Paging_Compose()
    {
        using var host = Boot();
        var c = host.Client("admin");

        var body = await GetAsync(c, ReleasesUrl(
            "?where[x.versionCode>=2].orderBy[x.versionCode].skip[1].take[5]"));

        Codes(body).Should().Equal(3, 4);
    }

    [Fact]
    public async Task Count_ReturnsTheRowCount()
    {
        using var host = Boot();
        var body = await GetAsync(host.Client("admin"), ReleasesUrl("?count[]"));

        body.ValueKind.Should().Be(JsonValueKind.Number);
        body.GetInt32().Should().Be(4);
    }

    private static readonly (int Code, string Name, bool Changelog)[] Versions =
    [
        (1, "1.0.0", false),
        (2, "1.1.0", true),
        (3, "2.0.0", true),
        (4, "2.1.0", false),
    ];

    private static TestHost Boot()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-releases-hlinq.db",
            DirPrefix = "fdroidmgr-releases-hlinq",
        });

        File.WriteAllText(Path.Combine(host.RepoDir, "index-v1.json"), JsonSerializer.Serialize(new
        {
            repo = new { name = "Test Repo", address = "http://localhost/fdroid/repo", timestamp = 0 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps = new object[]
            {
                new
                {
                    packageName = Pkg,
                    name = "Release App",
                    license = "MIT",
                    suggestedVersionCode = "2147483647",
                },
            },
            packages = new Dictionary<string, object[]>
            {
                [Pkg] =
                [
                    .. Versions.Select(v => new
                    {
                        versionCode = v.Code,
                        versionName = v.Name,
                        apkName = $"{Pkg}-{v.Code}.apk",
                        hash = new string('a', 64),
                        hashType = "sha256",
                        size = 1000L + v.Code,
                        minSdkVersion = 21,
                        targetSdkVersion = 34,
                    })
                ],
            },
        }));

        foreach (var v in Versions.Where(v => v.Changelog))
        {

            var dir = Path.Combine(host.Root, "metadata", Pkg, "en-US", "changelogs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, v.Code + ".txt"), $"changelog for {v.Name}");
        }

        host.Start();
        return host;
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string url)
    {
        var res = await c.GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string ReleasesUrl(string query = "") => $"/api/apps/{Pkg}/releases{query}";

    private static int[] Codes(JsonElement el) =>
        [.. el.EnumerateArray().Select(r => r.GetProperty("versionCode").GetInt32())];
}
