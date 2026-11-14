using FdroidMgr.Api.Services;
using FluentAssertions;

namespace FdroidMgr.Tests.Unit.Services;

public class KeyServiceTests
{
    [Fact]
    public void GetFingerprint_IsHexDigest_WhenKeytoolAvailable()
    {

        var tmpDir = Path.Combine(Path.GetTempPath(), "fdroidmgr-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var ks = Path.Combine(tmpDir, "test.keystore");
        try
        {
            var password = "TestPass123!";
            var psi = new System.Diagnostics.ProcessStartInfo("keytool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[]
            {
                "-genkeypair", "-storetype", "PKCS12", "-keystore", ks,
                "-storepass", password, "-keypass", password, "-alias", "release",
                "-keysize", "2048", "-keyalg", "RSA", "-validity", "100",
                "-dname", "CN=test"
            })
            {
                psi.ArgumentList.Add(a);
            }

            using var p = System.Diagnostics.Process.Start(psi);
            p!.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            p.ExitCode.Should().Be(0, "keytool must be able to create a keystore");

            var fp = KeyService.GetFingerprint(ks, password, "release");
            fp.Should().MatchRegex("^[0-9a-f]{64}$", "fingerprint is a 64-hex digest");
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void GetFingerprint_UnknownKey_ReturnsUnknown()
    {
        var fp = KeyService.GetFingerprint("/nonexistent.keystore", "pass", "alias");
        fp.Should().Be("unknown");
    }

    [Fact]
    public void ToManifestJson_SerializesPermissions()
    {
        var json = ApkService.ToManifestJson(new ApkMetadata
        {
            PackageName = "a.b",
            VersionCode = 1,
            VersionName = "1",
            MinSdk = 21,
            TargetSdk = 34,
            Permissions = ["android.permission.INTERNET"],
            Sha256 = "h",
        });
        json.Should().Contain("android.permission.INTERNET");
        json.Should().Contain("versionCode");
    }
}
