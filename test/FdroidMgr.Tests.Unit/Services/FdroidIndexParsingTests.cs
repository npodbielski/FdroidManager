using System.Text.Json;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class FdroidIndexParsingTests : IDisposable
{
    private readonly string _root;

    public FdroidIndexParsingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fdroidmgr-parse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
    }

    [Fact]
    public async Task ReadIndex_CorruptIndexFile_IsTreatedAsNoIndex()
    {
        File.WriteAllText(Path.Combine(_root, "repo", "index-v1.json"), "{ this is not json");

        (await Service().ReadIndexAsync(Cfg())).Should().BeNull();
    }

    [Fact]
    public async Task ReadIndex_AppsNotAList_IsTreatedAsNoIndex()
    {
        Write("""
            { "repo": { "name": "r" }, "apps": "nope" }
            """);

        (await Service().ReadIndexAsync(Cfg())).Should().BeNull();
    }

    [Fact]
    public async Task ReadV2Icons_CorruptFile_IsEmpty()
    {
        File.WriteAllText(Path.Combine(_root, "repo", "index-v2.json"), "[[[");

        (await Service().ReadV2IconsAsync(Cfg())).Should().BeEmpty();
    }

    [Fact]
    public void ParseV2Icons_PrefersMetadataIconAndEnglishLocale()
    {
        using var doc = JsonDocument.Parse("""
            { "packages": {
                "org.a": { "metadata": { "icon": { "de": { "name": "/de.png" }, "en-US": { "name": "/en.png" } } } },
                "org.b": { "icon": { "fr": { "name": "/fr.png" } } },
                "org.c": { "icon": { "en": { "name": "" } } },
                "org.d": null
            } }
            """);

        var icons = FdroidRepoService.ParseV2Icons(doc.RootElement);

        icons["org.a"].Should().Be("en.png");
        icons["org.b"].Should().Be("fr.png");
        icons.Should().NotContainKey("org.c");
        icons.Should().NotContainKey("org.d");
    }

    [Fact]
    public void Parse_Av1StyleAppsObject_UsesPropertyNamesAsPackages()
    {
        using var doc = JsonDocument.Parse("""
            { "repo": { "name": "old", "url": "http://old/repo", "timestamp": 5 },
              "apps": { "org.legacy": { "name": "Legacy", "license": "Apache-2.0" } },
              "packages": { "org.legacy": [ { "versionCode": 0 } ] } }
            """);

        var index = FdroidRepoService.Parse(doc.RootElement)!;

        index.RepoUrl.Should().Be("http://old/repo");
        index.Apps.Should().ContainSingle();
        index.Apps[0].PackageName.Should().Be("org.legacy");
        index.Apps[0].License.Should().Be("Apache-2.0");
        index.Apps[0].Versions.Should().BeEmpty("a version with versionCode 0 is not a release");
    }

    [Fact]
    public void Parse_EntryWithoutAnyPackageName_IsSkipped()
    {
        using var doc = JsonDocument.Parse("""
            { "repo": { "name": "r" }, "apps": [ { "name": "nameless" }, { "id": "org.by.id" } ] }
            """);

        var index = FdroidRepoService.Parse(doc.RootElement)!;

        index.Apps.Should().ContainSingle();
        index.Apps[0].PackageName.Should().Be("org.by.id");
    }

    [Fact]
    public void Parse_LegacyVersionObjects_ReadFileAndManifest()
    {
        using var doc = JsonDocument.Parse("""
            { "repo": { "name": "r" },
              "apps": [ { "packageName": "org.legacy", "versions": { "3": {
                    "versionCode": 3, "versionName": "1.3",
                    "file": { "name": "/org.legacy_3.apk", "size": 99, "hash": { "sha256": "abc" } },
                    "manifest": {
                      "usesSdk": { "minSdkVersion": 19, "targetSdkVersion": 30 },
                      "signer": { "sha256": [ "", "beef" ] },
                      "uses-permission": [ "android.permission.CAMERA" ] } } } } ] }
            """);

        var version = FdroidRepoService.Parse(doc.RootElement)!.Apps[0].Versions.Single();

        version.VersionCode.Should().Be(3);
        version.ApkName.Should().Be("org.legacy_3.apk");
        version.SizeBytes.Should().Be(99);
        version.Sha256.Should().Be("abc");
        version.SignerSha256.Should().Be("beef");
        version.MinSdk.Should().Be(19);
        version.TargetSdk.Should().Be(30);
        version.Permissions.Should().ContainSingle().Which.Should().Be("android.permission.CAMERA");
    }

    [Fact]
    public void Parse_SignerAsArray_TakesFirstUsableEntry()
    {
        using var doc = JsonDocument.Parse("""
            { "repo": { "name": "r" }, "apps": [ { "packageName": "org.a" } ],
              "packages": { "org.a": [ { "versionCode": 1, "signer": [ "", "cafe" ] } ] } }
            """);

        FdroidRepoService.Parse(doc.RootElement)!.Apps[0].Versions.Single().SignerSha256.Should().Be("cafe");
    }

    [Fact]
    public void Parse_ScreenshotsFallBackToAnyLocale()
    {
        using var doc = JsonDocument.Parse("""
            { "repo": { "name": "r" }, "apps": [ { "packageName": "org.a", "localized": {
                  "pl": { "phoneScreenshots": ["a.png"], "icon": "not-an-array" } } } ] }
            """);

        var app = FdroidRepoService.Parse(doc.RootElement)!.Apps[0];

        FdroidRepoService.PreferredScreenshots(app.Screenshots)
            .Should().ContainSingle().Which.Should().Be("org.a/pl/phoneScreenshots/a.png");
    }

    [Fact]
    public void PreferredScreenshots_EmptyOrNull_ReturnsNothing()
    {
        FdroidRepoService.PreferredScreenshots(null).Should().BeEmpty();
        FdroidRepoService.PreferredScreenshots(new Dictionary<string, string[]> { ["de"] = [] })
            .Should().BeEmpty();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Write(string json) => File.WriteAllText(Path.Combine(_root, "repo", "index-v1.json"), json);

    private RepoConfig Cfg() => new() { RepoPath = Path.Combine(_root, "repo") };

    private FdroidRepoService Service() => new(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot());
}
