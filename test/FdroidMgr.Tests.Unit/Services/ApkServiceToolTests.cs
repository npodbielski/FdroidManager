using System.IO.Compression;
using System.Text;
using FdroidMgr.Api.Abstractions;
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
    public void VerifySignature_ApksignerVerifies_ReadsDigest()
    {
        var apksigner = WriteTool("apksigner-ok.sh", """
            echo "Signer #1 certificate DN: CN=Test"
            echo "Signer #1 certificate SHA-256 digest: DEADBEEF0123456789abcdef0123456789abcdef0123456789abcdef0123"
            """);

        var sig = Service(apkSigner: apksigner).VerifySignature(CreateZipApk());

        sig.Status.Should().Be(ApkSignatureStatus.Verified);
        sig.Fingerprint.Should().StartWith("deadbeef0123");
    }

    [Fact]
    public void VerifySignature_FailsOnUnsigedApk_IsUnsigned()
    {
        var apksigner = WriteTool("apksigner-unsigned.sh", """
            echo "DOES NOT VERIFY" >&2
            echo "ERROR: Missing META-INF/MANIFEST.MF" >&2
            exit 1
            """);

        var sig = Service(apkSigner: apksigner).VerifySignature(CreateZipApk());

        sig.Status.Should().Be(ApkSignatureStatus.Unsigned);
    }

    [Fact]
    public void VerifySignature_FailsOnV1SignedApk_IsBroken()
    {
        var apksigner = WriteTool("apksigner-broken.sh", """
            echo "DOES NOT VERIFY" >&2
            echo "ERROR: JAR signature META-INF/RELEASE.SF indicates ... Signature stripped?" >&2
            exit 1
            """);

        var sig = Service(apkSigner: apksigner).VerifySignature(CreateSignedZipApk());

        sig.Status.Should().Be(ApkSignatureStatus.Broken);
        sig.Detail.Should().Be("DOES NOT VERIFY");
    }

    [Fact]
    public void VerifySignature_FailsOnV2SignedApk_IsBroken()
    {
        var apksigner = WriteTool("apksigner-broken-v2.sh", """
            echo "DOES NOT VERIFY" >&2
            echo "ERROR: Download code from v2 block" >&2
            exit 1
            """);

        var sig = Service(apkSigner: apksigner).VerifySignature(CreateV2BlockZipApk());

        sig.Status.Should().Be(ApkSignatureStatus.Broken);
    }

    [Fact]
    public void VerifySignature_CorruptArchive_IsBrokenNotCrash()
    {
        var apksigner = WriteTool("apksigner-corrupt.sh", """
            echo "DOES NOT VERIFY" >&2
            exit 1
            """);

        var junk = Path.Combine(_tmp, $"junk-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(junk, [1, 2, 3, 4, 5, 6, 7, 8]);

        var sig = Service(apkSigner: apksigner).VerifySignature(junk);

        sig.Status.Should().Be(ApkSignatureStatus.Broken);
    }

    [Fact]
    public void VerifySignature_TruncatedArchive_IsBrokenNotCrash()
    {
        var apksigner = WriteTool("apksigner-trunc.sh", """
            echo "DOES NOT VERIFY" >&2
            exit 1
            """);

        // a zip header with no central directory to find
        var truncated = Path.Combine(_tmp, $"trunc-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(truncated, [80, 75, 3, 4, 1, 2, 3]);

        var sig = Service(apkSigner: apksigner).VerifySignature(truncated);

        sig.Status.Should().Be(ApkSignatureStatus.Broken);
    }

    [Fact]
    public void VerifySignature_EmptyZip_IsUnsigned()
    {
        var apksigner = WriteTool("apksigner-tiny.sh", """
            echo "DOES NOT VERIFY" >&2
            exit 1
            """);

        // A zip, so it opens, but too small to have held a signing block.
        var tiny = Path.Combine(_tmp, $"tiny-{Guid.NewGuid():N}.apk");
        using (ZipFile.Open(tiny, ZipArchiveMode.Create))
        {
        }

        var sig = Service(apkSigner: apksigner).VerifySignature(tiny);

        sig.Status.Should().Be(ApkSignatureStatus.Unsigned);
    }

    [Fact]
    public void VerifySignature_ZipWithTrailingBytes_IsUnsigned()
    {
        var apksigner = WriteTool("apksigner-tail.sh", """
            echo "DOES NOT VERIFY" >&2
            exit 1
            """);

        // Opens as a zip, but the tail hides the central directory, so there is
        // nothing to mistake for a signing block.
        var tail = CreateZipApk();
        var path = Path.Combine(_tmp, $"tail-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(path, File.ReadAllBytes(tail).Concat(Enumerable.Repeat((byte)0x41, 30)).ToArray());

        var sig = Service(apkSigner: apksigner).VerifySignature(path);

        sig.Status.Should().Be(ApkSignatureStatus.Unsigned);
    }

    [Fact]
    public void VerifySignature_ApksignerMissing_Throws()
    {
        var svc = Service(apkSigner: Path.Combine(_tmp, "no-apksigner"));

        var act = () => svc.VerifySignature(CreateZipApk());
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

    // Carries the v1 pieces a signature leaves behind, without being one.
    private string CreateSignedZipApk()
    {
        var path = Path.Combine(_tmp, $"signed-{Guid.NewGuid():N}.apk");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        using (archive.CreateEntry("classes.dex").Open())
        {
        }

        using (archive.CreateEntry("META-INF/MANIFEST.MF").Open())
        {
        }

        using (archive.CreateEntry("META-INF/RELEASE.SF").Open())
        {
        }

        using (archive.CreateEntry("META-INF/RELEASE.RSA").Open())
        {
        }

        return path;
    }

    // Grows an APK Signing Block in front of the central directory, which is what
    // v2/v3 signing leaves behind when there is no META-INF signature.
    private string CreateV2BlockZipApk()
    {
        var bytes = File.ReadAllBytes(CreateZipApk());
        var eocd = IndexOfEndOfCentralDirectory(bytes);
        var centralDirectory = (int)BitConverter.ToUInt32(bytes, eocd + 16);

        // size of the block, then the magic immediately before the central directory
        var block = new byte[8 + 16];
        BitConverter.GetBytes((ulong)block.Length).CopyTo(block, 0);
        Encoding.ASCII.GetBytes("APK Sig Block 42").CopyTo(block, 8);

        var grown = new byte[bytes.Length + block.Length];
        bytes.AsSpan(0, centralDirectory).CopyTo(grown.AsSpan(0));
        block.CopyTo(grown.AsSpan(centralDirectory));
        bytes.AsSpan(centralDirectory).CopyTo(grown.AsSpan(centralDirectory + block.Length));

        BitConverter.GetBytes((uint)(centralDirectory + block.Length))
            .CopyTo(grown.AsSpan(eocd + block.Length + 16));

        var path = Path.Combine(_tmp, $"v2-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(path, grown);
        return path;
    }

    private static int IndexOfEndOfCentralDirectory(byte[] bytes)
    {
        for (var i = bytes.Length - 22; i >= 0; i--)
        {
            if (bytes[i] == 'P' && bytes[i + 1] == 'K' && bytes[i + 2] == 5 && bytes[i + 3] == 6)
            {
                return i;
            }
        }

        throw new InvalidDataException("not a zip file");
    }
}
