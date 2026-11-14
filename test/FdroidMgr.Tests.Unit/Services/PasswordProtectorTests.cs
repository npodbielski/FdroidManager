using System.Security.Cryptography;
using System.Text;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class PasswordProtectorTests
{
    private const string MasterKey = "b1-test-master-key-0123456789abcdef";

    [Fact]
    public async Task Protect_V2_RoundTrips_AndPersistsSaltOnce()
    {
        var db = TestDb.Create();
        var protector = Create(db);

        var blob = protector.Protect("hunter2");
        blob.Should().StartWith("v2:");
        blob.Should().NotContain("hunter2");
        protector.Unprotect(blob).Should().Be("hunter2");
        protector.IsLegacy(blob).Should().BeFalse();

        var salt = (await db.RepoConfig.FirstAsync()).MasterKeySalt;
        salt.Should().NotBeNullOrEmpty();

        var other = Create(TestDb.Reattach(db));
        other.Unprotect(blob).Should().Be("hunter2");
        (await db.RepoConfig.FirstAsync()).MasterKeySalt.Should().Be(salt);
    }

    [Fact]
    public void Protect_Unprotect_TamperedV2_Throws()
    {
        var db = TestDb.Create();
        var protector = Create(db);

        var blob = protector.Protect("secret-value");
        var parts = blob[3..].Split(':', 3);
        var cipher = Convert.FromBase64String(parts[2]);
        cipher[0] ^= 0xFF;
        var tampered = $"v2:{parts[0]}:{parts[1]}:{Convert.ToBase64String(cipher)}";

        var act = () => protector.Unprotect(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_LegacyCbcBlob_DecryptsWithLegacyKey()
    {
        var db = TestDb.Create();
        var protector = Create(db);
        var legacy = LegacyCbc(MasterKey, "old-keystore-pass");

        protector.IsLegacy(legacy).Should().BeTrue();
        protector.Unprotect(legacy).Should().Be("old-keystore-pass");
    }

    [Fact]
    public async Task GetAvailableKeyAsync_LegacyBlob_TransparentlyReprotectsToV2()
    {
        var db = TestDb.Create();
        await TestDb.SeedRepoConfigAsync(db, Path.GetTempPath(), Path.GetTempPath());
        db.Keys.Add(new AppKey
        {
            PackageName = "org.legacy.demo",
            Alias = "release",
            StorePath = "release.keystore",
            FingerprintSha256 = "ab",
            PasswordEncrypted = LegacyCbc(MasterKey, "legacy-pass"),
        });
        await db.SaveChangesAsync();

        var protector = Create(db);
        var svc = new KeyService(db, protector, TestIo.Real, new FakeEnvironmentService());

        var (_, password) = await svc.GetAvailableKeyAsync("org.legacy.demo", null, TestContext.Current.CancellationToken);
        password.Should().Be("legacy-pass");

        db.ChangeTracker.Clear();
        var reloaded = await db.Keys.SingleAsync();
        reloaded.PasswordEncrypted.Should().StartWith("v2:");
        protector.Unprotect(reloaded.PasswordEncrypted).Should().Be("legacy-pass");
    }

    private static PasswordProtector Create(FdroidDb db)
    {
        var env = new FakeEnvironmentService { Vars = { ["FDROID_MASTER_KEY"] = MasterKey } };
        return new PasswordProtector(db, new ConfigurationBuilder().Build(), env);
    }

    private static string LegacyCbc(string masterPass, string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(masterPass));
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plaintext), 0, plaintext.Length);
        return Convert.ToBase64String(aes.IV) + ":" + Convert.ToBase64String(cipher);
    }
}
