using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class KeyServiceArgvTests : IDisposable
{
    private const string MasterKey = "b2-test-master-key-0123456789abcdef";

    private readonly string _dir;
    private readonly string? _previousPath = Environment.GetEnvironmentVariable("PATH");

    public KeyServiceArgvTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"fdroidmgr-argv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task GenerateKeyAsync_KeytoolGetsPassphraseViaEnv_NotArgv()
    {
        var realKeytool = File.Exists("/usr/bin/keytool")
            ? "/usr/bin/keytool"
            : Environment.GetEnvironmentVariable("REAL_KEYTOOL");
        if (realKeytool is null || !File.Exists(realKeytool))
        {
            return;
        }

        var log = StubTool("keytool", realKeytool);
        Environment.SetEnvironmentVariable("PATH", _dir + Path.PathSeparator + _previousPath);
        var (svc, db) = Create();
        await TestDb.SeedRepoConfigAsync(db, _dir, _dir);

        var key = await svc.GenerateKeyAsync("org.argv.test", null, TestContext.Current.CancellationToken);

        var lines = File.ReadAllLines(log);
        var gen = lines.First(l => l.StartsWith("genkeypair args:"));
        gen.Should().Contain("-storepass:env").And.Contain("-keypass:env");
        var secret = lines.First(l => l.StartsWith("genkeypair-env: FDROID_KEYPASS="))["genkeypair-env: FDROID_KEYPASS=".Length..];
        secret.Should().NotBeNullOrEmpty();
        gen.Should().NotContain(secret);
        key.FingerprintSha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task SignApkAsync_ApkSignerGetsPassphraseViaEnv_NotArgv()
    {
        var log = StubTool("apksigner");
        Environment.SetEnvironmentVariable("APKSIGNER", Path.Combine(_dir, "apksigner"));
        var (svc, db) = Create();

        const string password = "s3cret-signing-pass";
        var protector = CreateProtector(db);
        db.Keys.Add(new AppKey
        {
            PackageName = "org.argv.test",
            Alias = "release",
            StorePath = "release.keystore",
            FingerprintSha256 = "ab",
            PasswordEncrypted = protector.Protect(password),
        });
        await db.SaveChangesAsync();

        await svc.SignApkAsync("org.argv.test", "/tmp/in.apk", "/tmp/out.apk", null, TestContext.Current.CancellationToken);

        var line = File.ReadAllLines(log).Single(l => l.StartsWith("sign args:"));
        line.Should().Contain("--ks-pass env:FDROID_KS_PASS").And.Contain("--key-pass env:FDROID_KEY_PASS");
        line.Should().NotContain("pass:").And.NotContain(password);
        File.ReadAllText(log).Should().Contain($"sign-env: FDROID_KS_PASS={password}");
    }

    private (KeyService Svc, FdroidDb Db) Create()
    {
        var db = TestDb.Create();
        var protector = CreateProtector(db);
        return (new KeyService(db, protector, TestIo.Real, new FakeEnvironmentService()), db);
    }

    private static PasswordProtector CreateProtector(FdroidDb db)
    {
        var env = new FakeEnvironmentService { Vars = { ["FDROID_MASTER_KEY"] = MasterKey } };
        return new PasswordProtector(db, new ConfigurationBuilder().Build(), env);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _previousPath);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string StubTool(string name, string? passthrough = null)
    {
        var log = Path.Combine(_dir, name + ".log");
        var script = Path.Combine(_dir, name);
        var varName = name == "keytool" ? "FDROID_KEYPASS" : "FDROID_KS_PASS";
        var mode = name == "keytool" ? "genkeypair" : "sign";
        var tail = passthrough is not null
            ? $"exec {passthrough} \"$@\""
            : "prev=\"\"; for a in \"$@\"; do if [ \"$prev\" = \"--out\" ]; then touch \"$a\"; fi; prev=\"$a\"; done";
        var body = string.Join("\n",
            "#!/bin/sh",
            "printf \"" + mode + " args: $*\\n\" >> " + log,
            "printf \"" + mode + "-env: " + varName + "=$" + varName + "\\n\" >> " + log,
            tail,
            "exit 0");
        File.WriteAllText(script, body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return log;
    }
}
