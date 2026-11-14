using System.Net;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Apps;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class AppsHlinqApiTests
{
    [Fact]
    public async Task NoClause_ReturnsEveryAppInPackageNameOrder()
    {
        using var host = Boot();
        var body = await GetAsync(host.Client("admin"), "/api/apps");

        Packages(body).Should().Equal(Apps.Select(a => a.Pkg).OrderBy(p => p, StringComparer.Ordinal));
        body.EnumerateArray().First().GetProperty("versionCount").GetInt32().Should().Be(2);
        body.EnumerateArray().First().GetProperty("latestVersion").GetString().Should().Be("1.2");
    }

    [Fact]
    public async Task Where_ContainsIsOrdinal()
    {
        using var host = Boot();
        var c = host.Client("admin");

        Packages(await GetAsync(c, "/api/apps?where[x.name.contains(Notes)]"))
            .Should().Equal("org.example.alpha", "org.example.delta");
        Packages(await GetAsync(c, "/api/apps?where[x.license.contains(MIT)].orderBy[x.packageName]"))
            .Should().Equal("org.example.beta", "org.example.delta");

        Packages(await GetAsync(c, "/api/apps?where[x.name.contains(notes)]")).Should().BeEmpty();
    }

    [Fact]
    public async Task Where_Or_CaseInsensitive_MatchesNamePackageOrSummary()
    {
        using var host = Boot();
        var c = host.Client("admin");

        string Ui(string term) => $"/api/apps?where[(x.name.contains({term}, StringComparison.InvariantCultureIgnoreCase) || x.packageName.contains({term}, StringComparison.InvariantCultureIgnoreCase) || x.summary.contains({term}, StringComparison.InvariantCultureIgnoreCase))]";

        Packages(await GetAsync(c, Ui("notes"))).Should().Equal("org.example.alpha", "org.example.delta");
        Packages(await GetAsync(c, Ui("NOTES"))).Should().Equal("org.example.alpha", "org.example.delta");

        Packages(await GetAsync(c, Ui("TIMER"))).Should().Equal("org.example.beta");
        Packages(await GetAsync(c, Ui("org.example.gam"))).Should().Equal("org.example.gamma");
        Packages(await GetAsync(c, Ui("clock"))).Should().Equal("org.example.gamma");

        Packages(await GetAsync(c, Ui("more notes"))).Should().Equal("org.example.delta");

        Packages(await GetAsync(c, Ui("Clock"))).Should().Equal("org.example.gamma");
    }

    [Fact]
    public async Task FullUiQuery_WhereAndSortAndPage()
    {
        using var host = Boot();
        var c = host.Client("admin");

        const string q = "/api/apps?where[(x.name.contains(notes, StringComparison.InvariantCultureIgnoreCase) || x.packageName.contains(notes, StringComparison.InvariantCultureIgnoreCase) || x.summary.contains(notes, StringComparison.InvariantCultureIgnoreCase))]"
            + ".orderByDescending[x.name].skip[1].take[2]";
        Packages(await GetAsync(c, q)).Should().Equal("org.example.alpha");
    }

    [Fact]
    public async Task OrderBy_ThenBy_Skip_Take()
    {
        using var host = Boot();
        var c = host.Client("admin");

        Packages(await GetAsync(c, "/api/apps?orderBy[x.name]"))
            .Should().Equal("org.example.alpha", "org.example.beta", "org.example.delta", "org.example.gamma");
        Packages(await GetAsync(c, "/api/apps?orderByDescending[x.versionCount].thenBy[x.name]"))
            .Should().Equal("org.example.delta", "org.example.alpha", "org.example.beta", "org.example.gamma");
        Packages(await GetAsync(c, "/api/apps?orderBy[x.packageName].skip[1].take[2]"))
            .Should().Equal("org.example.beta", "org.example.delta");
    }

    [Fact]
    public async Task Count_CountsWhatThePrecedingClausesProduced()
    {
        using var host = Boot();
        var c = host.Client("admin");

        (await GetAsync(c, "/api/apps?count[]")).GetInt32().Should().Be(Apps.Length);
        (await GetAsync(c, "/api/apps?where[x.license.contains(MIT)].count[]")).GetInt32().Should().Be(2);

        (await GetAsync(c, "/api/apps?take[1].count[]")).GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task MetadataOnlyApps_Participate()
    {
        using var host = Boot();

        Directory.CreateDirectory(Path.Combine(host.Root, "metadata"));
        File.WriteAllText(Path.Combine(host.Root, "metadata", "org.example.fresh.yml"),
            "AntiFeatures: {}\nCategories:\n  - Utility\nLicense: GPL-3.0\nName: Fresh Notes\nSummary: not published yet\n");

        var c = host.Client("admin");
        var body = await GetAsync(c, "/api/apps?where[x.name.contains(Fresh)]");
        var fresh = body.EnumerateArray().Single();
        fresh.GetProperty("packageName").GetString().Should().Be("org.example.fresh");
        fresh.GetProperty("versionCount").GetInt32().Should().Be(0);
        fresh.GetProperty("latestVersion").ValueKind.Should().Be(JsonValueKind.Null);

        Packages(await GetAsync(c, "/api/apps?orderBy[x.versionCount].take[1]")).Should().Equal("org.example.fresh");
    }

    [Fact]
    public async Task MalformedQuery_IsBadRequestProblemDetails()
    {
        using var host = Boot();

        var res = await host.Client("admin").GetAsync("/api/apps?where[");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.TryGetProperty("title", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UnknownField_IsBadRequestNotFiveHundred()
    {
        using var host = Boot();
        var res = await host.Client("admin").GetAsync("/api/apps?where[x.sha256.contains(x)]");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DetailEndpoint_IsNotQueryAppliedTo()
    {
        using var host = Boot();
        var res = await host.Client("admin").GetAsync("/api/apps/org.example.beta?count[]");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static readonly (string Pkg, string Name, string Summary, string License, int[] Codes)[] Apps =
    [
        ("org.example.alpha", "Alpha Notes", "notes app", "GPL-3.0", [1, 2]),
        ("org.example.beta", "Beta Timer", "Timer App", "MIT", [5]),
        ("org.example.gamma", "Gamma Clock", "clock", "Apache-2.0", [7]),
        ("org.example.delta", "Delta Notes", "more notes", "MIT", [3, 4, 9]),
    ];

    private static TestHost Boot()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-apps-hlinq.db",
            DirPrefix = "fdroidmgr-apps-hlinq",
        });

        var index = JsonSerializer.Serialize(new
        {
            repo = new { name = "Test Repo", address = "http://localhost/fdroid/repo", timestamp = 0 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps = Apps.Select(a => (object)new
            {
                packageName = a.Pkg,
                name = a.Name,
                summary = a.Summary,
                description = a.Summary,
                license = a.License,
            }).ToArray(),
            packages = Apps.ToDictionary(
                a => a.Pkg,
                a => (object[])
                [
                    .. a.Codes.Select(c => new
                    {
                        versionCode = c, versionName = $"1.{c}", apkName = $"{a.Pkg}-{c}.apk",
                        hash = new string('a', 64), hashType = "sha256", size = 1000L
                    })
                ]),
        });
        File.WriteAllText(Path.Combine(host.RepoDir, "index-v1.json"), index);

        host.Start();
        return host;
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string url)
    {
        var res = await c.GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Packages(JsonElement el) =>
        [.. el.EnumerateArray().Select(a => a.GetProperty("packageName").GetString()!)];
}
