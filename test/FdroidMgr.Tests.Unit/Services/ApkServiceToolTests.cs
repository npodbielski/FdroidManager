using System.IO.Compression;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class ApkServiceToolTests : IDisposable
{
    private readonly string _tmp;

    private readonly ILogger<ApkService> _log = Substitute.For<ILogger<ApkService>>();

    public ApkServiceToolTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fdroidmgr-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [Fact]
    public async Task ReadAsync_AaptPresent_UsesBadgingDump()
    {
        var aapt = WriteTool("aapt-ok.sh", """
            echo "package: name='org.tool.demo' versionCode='9' versionName='3.0.0' platformBuildVersionName='14'"
            echo "sdkVersion:'21'"
            echo "targetSdkVersion:'33'"
            echo "application-label:'Tool Demo'"
            echo "uses-permission: name='android.permission.INTERNET'"
            """);

        var meta = await Service(aapt).ReadAsync(CreateZipApk(), TestContext.Current.CancellationToken);

        meta.PackageName.Should().Be("org.tool.demo");
        meta.VersionCode.Should().Be(9);
        meta.VersionName.Should().Be("3.0.0");
        meta.Label.Should().Be("Tool Demo");
        meta.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task ReadAsync_AaptFails_FallsBackToManagedZipRead()
    {
        var aapt = WriteTool("aapt-bad.sh", """
            echo "badging exploded" >&2
            exit 1
            """);

        var meta = await Service(aapt).ReadAsync(CreateZipApk(), TestContext.Current.CancellationToken);

        meta.VersionName.Should().Be("?", "the aapt failure must fall through to the managed zip reader");
        meta.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void SignerFingerprint_ApksignerPresent_ReadsDigest()
    {
        var apksigner = WriteTool("apksigner-ok.sh", """
            echo "Signer #1 certificate DN: CN=Test"
            echo "Signer #1 certificate SHA-256 digest: DEADBEEF0123456789abcdef0123456789abcdef0123456789abcdef0123"
            """);

        Service(apkSigner: apksigner).SignerFingerprint(CreateZipApk()).Should().StartWith("deadbeef0123");
    }

    [Fact]
    public void SignerFingerprint_ApksignerWithoutDigest_IsUnknown()
    {
        var apksigner = WriteTool("apksigner-empty.sh", "echo 'no digests here'");

        Service(apkSigner: apksigner).SignerFingerprint(CreateZipApk()).Should().Be("unknown");
    }

    [Fact]
    public void SignerFingerprint_ApksignerMissing_Throws()
    {
        var svc = Service(apkSigner: Path.Combine(_tmp, "no-apksigner"));

        var act = () => svc.SignerFingerprint(CreateZipApk());
        act.Should().Throw<InvalidOperationException>().WithMessage("*apksigner not found*");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private ApkService Service(string? aapt = null, string? apkSigner = null)
    {
        var env = new FakeEnvironmentService
        {
            Vars =
            {
                ["AAPT"] = aapt ?? Path.Combine(_tmp, "no-aapt"),
                ["APKSIGNER"] = apkSigner ?? Path.Combine(_tmp, "no-apksigner"),
            },
        };

        return new ApkService(_log, TestIo.Real, env);
    }

    private string WriteTool(string name, string body)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return path;
    }

    private string CreateZipApk()
    {
        var path = Path.Combine(_tmp, $"app-{Guid.NewGuid():N}.apk");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        using (var manifest = archive.CreateEntry("AndroidManifest.xml").Open())
        using (var writer = new StreamWriter(manifest))
        {
            writer.Write("<?xml version='1.0'?><manifest package='org.zip.demo' versionCode='3'/>");
        }

        using (archive.CreateEntry("classes.dex").Open())
        {
        }

        return path;
    }
}
