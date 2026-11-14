using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class FdroidRepoServiceTests
{
    private const string Pkg = "org.example.helloworld";

    [Fact]
    public void Parse_ReadsRepoAppAndVersionFromV1Format()
    {
        using var doc = JsonDocument.Parse(V1Json());

        var index = FdroidRepoService.Parse(doc.RootElement);

        index.Should().NotBeNull();
        index.RepoName.Should().Be("My Repo");
        index.RepoUrl.Should().Be("http://localhost/fdroid/repo");
        index.Timestamp.Should().Be(1787760032000);
        index.Apps.Should().ContainSingle();

        var app = index.Apps[0];
        app.PackageName.Should().Be(Pkg);
        app.Name.Should().Be("Hello World");
        app.Summary.Should().Be("A demo app");
        app.Description.Should().Be("Longer text");
        app.License.Should().Be("MIT");
        app.Web.Should().Be("https://example.com");
        app.Source.Should().Be("https://git.example.com/a/b");
        app.Tracker.Should().Be("https://git.example.com/a/b/-/issues");

        app.Versions.Should().ContainSingle();
        var v = app.Versions[0];
        v.VersionCode.Should().Be(2);
        v.VersionName.Should().Be("1.0.0");
        v.ApkName.Should().Be($"{Pkg}_2.apk");
        v.Sha256.Should().Be("9b13bbd6ea6382a948b2a0e016a2836a731518b0cf3b4bc5253810832108f24b");
        v.SizeBytes.Should().Be(16865);
        v.SignerSha256.Should().Be("2045fac37f513b87291bad263ac3590e56f4116ba949510f63028dfa5416c7b1");
        v.MinSdk.Should().Be(21);
        v.TargetSdk.Should().Be(34);
        v.Permissions.Should().Contain("android.permission.INTERNET");
    }

    [Fact]
    public void Parse_ToleratesFdroid2xUsesPermissionPairs()
    {

        using var doc = JsonDocument.Parse(V1Json(perms: new object[]
        {
            new object?[] { "android.permission.INTERNET", null },
            new object?[] { "android.permission.ACCESS_NETWORK_STATE", null },
        }));

        FdroidRepoService.Parse(doc.RootElement)!.Apps[0].Versions[0].Permissions
            .Should().BeEquivalentTo("android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE");
    }

    [Fact]
    public void ParseV2Icons_ReadsMetadataIcon_PreferringEnUs()
    {

        var json = JsonSerializer.Serialize(new
        {
            packages = new Dictionary<string, object>
            {
                ["org.example.app"] = new
                {
                    metadata = new
                    {
                        icon = new Dictionary<string, object>
                        {
                            ["pl-PL"] = new { name = "/org.example.app/pl-PL/icon_pl.png", sha256 = "x", size = 1 },
                            ["en-US"] = new { name = "/org.example.app/en-US/icon_en.png", sha256 = "x", size = 1 },
                        },
                    },
                },
            },
        });

        using var doc = JsonDocument.Parse(json);
        var icons = FdroidRepoService.ParseV2Icons(doc.RootElement);
        icons.Count.Should().Be(1);
        icons["org.example.app"].Should().Be("org.example.app/en-US/icon_en.png");
    }

    [Fact]
    public void Parse_ToleratesCamelCasePermissionKey()
    {

        using var doc = JsonDocument.Parse(V1Json(permKey: "usesPermission"));
        FdroidRepoService.Parse(doc.RootElement)!.Apps[0].Versions[0].Permissions
            .Should().Contain("android.permission.INTERNET");
    }

    [Fact]
    public void Parse_ReadsIconAndLocalizedScreenshots()
    {

        var json = JsonSerializer.Serialize(new
        {
            repo = new { name = "My Repo", address = "http://localhost/fdroid/repo", timestamp = 1787760032000 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps = new object[]
            {
                new
                {
                    packageName = Pkg,
                    name = "Hello World",
                    summary = "A demo app",
                    description = "Longer text",
                    license = "MIT",
                    icon = $"{Pkg}.2.png",
                    localized = new Dictionary<string, object>
                    {
                        ["en-US"] = new Dictionary<string, object>
                        {
                            ["phoneScreenshots"] = new[] { "one.png", "two.png" },
                            ["sevenInchScreenshots"] = new[] { "tablet.png" },
                        },
                        ["de-DE"] = new Dictionary<string, object>
                        {
                            ["tvScreenshots"] = new[] { "tv.png" },
                        },
                    },
                },
            },
            packages = new Dictionary<string, object[]>
            {
                [Pkg] =
                [
                    new Dictionary<string, object?>
                    {
                        ["versionCode"] = 2,
                        ["versionName"] = "1.0.0",
                        ["apkName"] = $"{Pkg}_2.apk",
                        ["hash"] = "9b13bbd6ea6382a948b2a0e016a2836a731518b0cf3b4bc5253810832108f24b",
                        ["size"] = 16865,
                    }
                ],
            },
        });

        using var doc = JsonDocument.Parse(json);
        var app = FdroidRepoService.Parse(doc.RootElement)!.Apps.Should().ContainSingle().Subject;

        app.Icon.Should().Be($"{Pkg}.2.png");

        app.Versions.Should().ContainSingle();
        app.Versions[0].VersionCode.Should().Be(2);

        app.Screenshots.Should().NotBeNull();
        app.Screenshots!["en-US"].Should().Equal(
            $"{Pkg}/en-US/phoneScreenshots/one.png",
            $"{Pkg}/en-US/phoneScreenshots/two.png",
            $"{Pkg}/en-US/sevenInchScreenshots/tablet.png");
        app.Screenshots!["de-DE"].Should().Equal($"{Pkg}/de-DE/tvScreenshots/tv.png");

        FdroidRepoService.PreferredScreenshots(app.Screenshots).Should().Equal(
            $"{Pkg}/en-US/phoneScreenshots/one.png",
            $"{Pkg}/en-US/phoneScreenshots/two.png",
            $"{Pkg}/en-US/sevenInchScreenshots/tablet.png");
    }

    [Fact]
    public void Parse_WithoutMedia_LeavesIconAndScreenshotsEmpty()
    {
        using var doc = JsonDocument.Parse(V1Json());
        var app = FdroidRepoService.Parse(doc.RootElement)!.Apps[0];

        app.Icon.Should().BeNull();
        app.Screenshots.Should().BeNull();
        FdroidRepoService.PreferredScreenshots(app.Screenshots).Should().BeEmpty();
    }

    [Fact]
    public void PreferredScreenshots_PrefersEnUs_ThenEn_ThenFirstLocale()
    {
        var shots = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["fr-FR"] = [$"{Pkg}/fr-FR/phoneScreenshots/fr.png"],
            ["en"] = [$"{Pkg}/en/phoneScreenshots/en.png"],
            ["en-US"] = [$"{Pkg}/en-US/phoneScreenshots/en-us.png"],
        };

        FdroidRepoService.PreferredScreenshots(shots).Should().Equal($"{Pkg}/en-US/phoneScreenshots/en-us.png");
    }

    [Fact]
    public void LastVersionCode_ReadsMaxFromIndex_ZeroWhenMissing()
    {
        using var doc = JsonDocument.Parse(V1Json());
        var index = FdroidRepoService.Parse(doc.RootElement)!;

        FdroidRepoService.LastVersionCode(index, Pkg).Should().Be(2);
        FdroidRepoService.LastVersionCode(index, "org.example.nope").Should().Be(0);
        FdroidRepoService.LastVersionCode(null, Pkg).Should().Be(0);
    }

    [Fact]
    public void LastVersion_ReturnsHighestVersionCode_OrNull()
    {
        using var doc = JsonDocument.Parse(V1Json());
        var index = FdroidRepoService.Parse(doc.RootElement)!;

        FdroidRepoService.LastVersion(index, Pkg)!.VersionCode.Should().Be(2);
        FdroidRepoService.LastVersion(index, "org.example.nope").Should().BeNull();
        FdroidRepoService.LastVersion(null, Pkg).Should().BeNull();
    }

    [Theory]
    [InlineData("1.0.3", "1.0.4")]
    [InlineData("1.0", "1.0.1")]
    [InlineData("1.0.3+42", "1.0.4")]
    [InlineData("2.5.9", "2.5.10")]
    [InlineData("1.0.0-beta1", "1.0.1")]
    [InlineData(" 1.0.3 ", "1.0.4")]
    public void NextVersionName_IncrementsPatch(string current, string expected)
        => FdroidRepoService.NextVersionName(current).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("1")]
    [InlineData("v1.0.3")]
    public void NextVersionName_Unparseable_ReturnsNull(string? current)
        => FdroidRepoService.NextVersionName(current).Should().BeNull();

    [Fact]
    public void NextVersion_DerivesFromPreviousRelease()
    {
        using var doc = JsonDocument.Parse(V1Json());
        var index = FdroidRepoService.Parse(doc.RootElement)!;

        var next = FdroidRepoService.NextVersion(index, Pkg);
        next.VersionName.Should().Be("1.0.1");
        next.VersionCode.Should().Be(3);

        var none = FdroidRepoService.NextVersion(index, "org.example.nope");
        none.VersionName.Should().Be("1.0.0");
        none.VersionCode.Should().Be(1);
    }

    [Fact]
    public void Parse_MissingApps_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"repo\":{}}");
        FdroidRepoService.Parse(doc.RootElement).Should().BeNull();
    }

    [Fact]
    public void ChangelogPath_UsesFdroidNativeLocation()
    {
        FdroidRepoService.ChangelogPath("/data/fdroidrepo", Pkg, 2)
            .Should().Be(Path.Combine("/data/fdroidrepo", "metadata", Pkg, "en-US", "changelogs", "2.txt"));
    }

    [Fact]
    public async Task Changelog_WriteAndRead_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdroidmgr-repo-svc-" + Guid.NewGuid().ToString("N"));
        var repoDir = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoDir);
        try
        {
            var db = TestDb.Create();
            await TestDb.SeedRepoConfigAsync(db, repoDir, Path.Combine(root, "keys"));
            var cfg = await db.RepoConfig.FindAsync(1);
            var svc = new FdroidRepoService(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot());

            (await svc.ReadChangelogAsync(cfg!, Pkg, 2, TestContext.Current.CancellationToken)).Should().BeNull();

            await svc.WriteChangelogAsync(cfg!, Pkg, 2, "first release", TestContext.Current.CancellationToken);

            var path = FdroidRepoService.ChangelogPath(root, Pkg, 2);
            File.Exists(path).Should().BeTrue();
            (await File.ReadAllTextAsync(path)).Should().Be("first release");
            (await svc.ReadChangelogAsync(cfg!, Pkg, 2, TestContext.Current.CancellationToken)).Should().Be("first release");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ReadIndexAsync_ReturnsNull_WhenFileMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdroidmgr-repo-svc-" + Guid.NewGuid().ToString("N"));
        var repoDir = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoDir);
        var previousEnv = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);
            var cfg = new RepoConfig { RepoPath = repoDir };
            var svc = new FdroidRepoService(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot());

            (await svc.ReadIndexAsync(cfg, TestContext.Current.CancellationToken)).Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", previousEnv);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ReadIndexAsync_ParsesOncePerMtime_AndReReadsAfterARebuild()
    {
        var previousEnv = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);
            var cfg = new RepoConfig { RepoPath = "/data/repo" };
            var path = FdroidRepoService.IndexPath(RepoIndexService.ResolveRepoRoot(cfg));
            var bytes = Encoding.UTF8.GetBytes(V1Json());

            var io = Substitute.For<IOService>();
            io.FileExists(path).Returns(true);
            io.GetLastWriteTimeUtc(path).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            io.OpenRead(path).Returns(_ => new MemoryStream(bytes));

            var svc = new FdroidRepoService(NullLogger<FdroidRepoService>.Instance, io, new RepoIndexSnapshot());

            (await svc.ReadIndexAsync(cfg, TestContext.Current.CancellationToken)).Should().NotBeNull();
            (await svc.ReadIndexAsync(cfg, TestContext.Current.CancellationToken)).Should().NotBeNull();
            io.Received(1).OpenRead(path);

            io.GetLastWriteTimeUtc(path).Returns(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc));
            (await svc.ReadIndexAsync(cfg, TestContext.Current.CancellationToken)).Should().NotBeNull();
            io.Received(2).OpenRead(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", previousEnv);
        }
    }

    [Fact]
    public async Task ReadV2IconsAsync_ParsesOncePerMtime()
    {
        var previousEnv = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);
            var cfg = new RepoConfig { RepoPath = "/data/repo" };
            var path = Path.Combine(RepoIndexService.ResolveRepoRoot(cfg), "repo", "index-v2.json");
            var bytes = """{"packages":{"org.example.helloworld":{"icon":{"en-US":{"name":"/icon.png"}}}}}"""u8.ToArray();

            var io = Substitute.For<IOService>();
            io.FileExists(path).Returns(true);
            io.GetLastWriteTimeUtc(path).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            io.OpenRead(path).Returns(_ => new MemoryStream(bytes));

            var svc = new FdroidRepoService(NullLogger<FdroidRepoService>.Instance, io, new RepoIndexSnapshot());

            var first = await svc.ReadV2IconsAsync(cfg, TestContext.Current.CancellationToken);
            var second = await svc.ReadV2IconsAsync(cfg, TestContext.Current.CancellationToken);

            first.Should().ContainKey("org.example.helloworld");
            second.Should().Equal(first);
            io.Received(1).OpenRead(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", previousEnv);
        }
    }

    [Fact]
    public void Snapshot_KeysOnPathAndMtime()
    {
        IRepoIndexSnapshot snap = new RepoIndexSnapshot();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        snap.TryGetIndex("/a/index-v1.json", stamp, out _).Should().BeFalse();
        snap.SetIndex("/a/index-v1.json", stamp, null);
        snap.TryGetIndex("/a/index-v1.json", stamp, out var hit).Should().BeTrue("a cached miss is still a hit");
        hit.Should().BeNull();

        snap.TryGetIndex("/a/index-v1.json", stamp.AddSeconds(1), out _).Should().BeFalse();
        snap.TryGetIndex("/b/index-v1.json", stamp, out _).Should().BeFalse();

        var icons = new Dictionary<string, string> { ["p"] = "i.png" };
        snap.TryGetV2Icons("/a/index-v2.json", stamp, out _).Should().BeFalse();
        snap.SetV2Icons("/a/index-v2.json", stamp, icons);
        snap.TryGetV2Icons("/a/index-v2.json", stamp, out var cached).Should().BeTrue();
        cached.Should().BeSameAs(icons);
        snap.TryGetV2Icons("/a/index-v2.json", stamp.AddSeconds(1), out _).Should().BeFalse();
    }

    private static string V1Json(string? packageName = "org.example.helloworld", string permKey = "uses-permission", object? perms = null)
    {
        var version = new Dictionary<string, object?>
        {
            ["versionCode"] = 2,
            ["versionName"] = "1.0.0",
            ["apkName"] = $"{packageName}_2.apk",
            ["hash"] = "9b13bbd6ea6382a948b2a0e016a2836a731518b0cf3b4bc5253810832108f24b",
            ["hashType"] = "sha256",
            ["size"] = 16865,
            ["minSdkVersion"] = 21,
            ["targetSdkVersion"] = 34,
            ["signer"] = "2045fac37f513b87291bad263ac3590e56f4116ba949510f63028dfa5416c7b1",
            [permKey] = perms ?? new[] { "android.permission.INTERNET" },
        };
        return JsonSerializer.Serialize(new
        {
            repo = new { name = "My Repo", address = "http://localhost/fdroid/repo", timestamp = 1787760032000 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps = new object[]
            {
                new
                {
                    packageName,
                    name = "Hello World",
                    summary = "A demo app",
                    description = "Longer text",
                    license = "MIT",
                    webSite = "https://example.com",
                    sourceCode = "https://git.example.com/a/b",
                    issueTracker = "https://git.example.com/a/b/-/issues",
                    added = 1787733015000,
                    lastUpdated = 1787733015000,
                },
            },
            packages = new Dictionary<string, object[]>
            {
                [packageName!] = [version],
            },
        });
    }
}
