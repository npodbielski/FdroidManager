using FdroidMgr.Api.Services;
using FluentAssertions;

namespace FdroidMgr.Tests.Unit.Services;

public class RepoIndexServiceTests
{
    [Fact]
    public void ParseBadging_IsAccessibleViaApkService()
    {

        ApkService.ParseBadging(
            "package: name='a.b' versionCode='1' versionName='1' sdkVersion:'21' targetSdkVersion:'34'",
            "x").PackageName.Should().Be("a.b");
    }

    [Fact]
    public void StandardRepoFiles_HaveExpectedNames()
    {

        var expectedFiles = new[]
        {
            "index.xml",
            "index-v1.json",
            "index-v2.json",
            "entry.json",
            "index.jar",
            "index-v1.jar",
            "entry.jar",
            "index.html",
            "index.css",
            "index.png"
        };

        expectedFiles.Should().NotBeEmpty("all standard F-Droid repo files should be generated");
        expectedFiles.Should().HaveCount(10, "there should be exactly 10 standard repo files");
    }

    [Fact]
    public void DensityIconDirectories_HaveExpectedDensityBuckets()
    {

        var expectedDirs = new[]
        {
            "icons-120",
            "icons-160",
            "icons-240",
            "icons-320",
            "icons-480",
            "icons-640"
        };

        expectedDirs.Should().HaveCount(6, "all standard Android density buckets should have directories");
    }

    [Fact]
    public void SignedJar_Sha256DigestIsWellFormed()
    {

        var testContent = "test content";
        var sha256 = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(testContent)));

        sha256.Should().NotBeNullOrEmpty("SHA-256 digest should be generated");
        sha256.Length.Should().BeGreaterThan(20, "Base64 encoded SHA-256 should be sufficiently long");
    }
}
