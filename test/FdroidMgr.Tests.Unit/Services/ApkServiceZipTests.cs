using System.IO.Compression;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class ApkServiceZipTests : IDisposable
{
    private readonly string _tmp;

    private readonly ILogger<ApkService> _log;

    public ApkServiceZipTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fdroidmgr-apk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _log = Substitute.For<ILogger<ApkService>>();
    }

    [Fact]
    public async Task ReadAsync_ManagedFallback_ReadsZipAndHashes()
    {
        var apk = CreateZipApk();
        var svc = new ApkService(_log, TestIo.Real, new FakeEnvironmentService());

        var meta = await svc.ReadAsync(apk, TestContext.Current.CancellationToken);

        meta.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");

        meta.VersionName.Should().Be("?");
    }

    [Fact]
    public async Task ReadAsync_MissingFile_Throws()
    {
        var svc = new ApkService(_log, TestIo.Real, new FakeEnvironmentService());
        var act = async () => await svc.ReadAsync(Path.Combine(_tmp, "nope.apk"), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ComputeSha256_StableAcrossCalls()
    {
        var apk = CreateZipApk();
        var h1 = await ApkService.ComputeSha256Async(apk, TestIo.Real, TestContext.Current.CancellationToken);
        var h2 = await ApkService.ComputeSha256Async(apk, TestIo.Real, TestContext.Current.CancellationToken);
        h1.Should().Be(h2);
        h1.Length.Should().Be(64);
    }

    [Fact]
    public void ApkSignerFingerprint_WhenAaptMissing_Throws()
    {
        var env = new FakeEnvironmentService
        {
            Vars = { ["ANDROID_HOME"] = "/nonexistent-sdk", ["APKSIGNER"] = null },
        };
        var svc = new ApkService(_log, TestIo.Real, env);
        var act = () => svc.ApkSignerFingerprint("/tmp/fake.apk");
        act.Should().Throw<InvalidOperationException>().WithMessage("*apksigner not found*");
    }

    [Fact]
    public void SignerFingerprint_UsesApkSignerPath()
    {
        var svc = new ApkService(_log, TestIo.Real, new FakeEnvironmentService());

        Environment.SetEnvironmentVariable("ANDROID_HOME", "/nonexistent");
        Environment.SetEnvironmentVariable("APKSIGNER", null);
        try
        {
            var act = () => svc.SignerFingerprint("/tmp/fake.apk");
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANDROID_HOME", null);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private string CreateZipApk(string package = "org.zip.demo", int versionCode = 3)
    {
        var path = Path.Combine(_tmp, "demo.apk");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        using (var manifest = zip.CreateEntry("AndroidManifest.xml").Open())
        using (var tw = new StreamWriter(manifest))
        {
            tw.Write("<?xml version='1.0'?><manifest package='");
            tw.Write(package);
            tw.Write("' versionCode='");
            tw.Write(versionCode);
            tw.Write("'/>");
        }

        using (var ds = zip.CreateEntry("classes.dex").Open())
        {
            ds.WriteByte(0x64);
        }

        return path;
    }
}
