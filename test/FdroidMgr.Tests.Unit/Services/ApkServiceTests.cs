using FdroidMgr.Api.Services;
using FluentAssertions;
using NSubstitute;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class ApkServiceTests
{
    [Fact]
    public void ParseBadging_ExtractsPackageVersionAndSdk()
    {
        var output = """
            package: name='org.example.demo' versionCode='7' versionName='2.3.1' platformBuildVersionName='14'
            sdkVersion:'21'
            targetSdkVersion:'34'
            application-label:'Demo'
            launchable-activity: name='org.example.demo.MainActivity'  label='' icon=''
            uses-permission: name='android.permission.INTERNET'
            uses-permission: name='android.permission.ACCESS_NETWORK_STATE'
            """;

        var m = ApkService.ParseBadging(output, "abc123");

        m.PackageName.Should().Be("org.example.demo");
        m.VersionCode.Should().Be(7);
        m.VersionName.Should().Be("2.3.1");
        m.MinSdk.Should().Be(21);
        m.TargetSdk.Should().Be(34);
        m.Label.Should().Be("Demo");
        m.LaunchableActivity.Should().Be("org.example.demo.MainActivity");
        m.Permissions.Should().BeEquivalentTo("android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE");
        m.Sha256.Should().Be("abc123");
    }

    [Fact]
    public void ParseBadging_HandlesMissingLines_Gracefully()
    {
        var m = ApkService.ParseBadging("package: name='org.x.demo' versionCode='1' versionName='1'", "h");
        m.PackageName.Should().Be("org.x.demo");
        m.MinSdk.Should().Be(0);
        m.Permissions.Should().BeEmpty();
        m.LaunchableActivity.Should().BeNull();
    }

    [Fact]
    public void ReadAsync_WhenFileMissing_Throws()
    {
        var svc = new ApkService(Substitute.For<Microsoft.Extensions.Logging.ILogger<ApkService>>(), TestIo.Real, new FakeEnvironmentService());
        var act = () => svc.ReadAsync("/nonexistent/path.apk");
        act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ComputeSha256_MatchesKnownHash()
    {
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "hello fdroid");
        try
        {
            var hash = await ApkService.ComputeSha256Async(tmp, TestIo.Real, CancellationToken.None);
            hash.Should().Be("ba44e30ebf443e6e825d5dc0fed1deba21a73975e9362a29add4ae956b8e303b");
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
