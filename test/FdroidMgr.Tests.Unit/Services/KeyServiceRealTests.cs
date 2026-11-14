using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class KeyServiceRealTests : IDisposable
{
    private readonly string _keyDir;

    public KeyServiceRealTests()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), "fdroidmgr-keyreal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_keyDir);
    }

    [Fact]
    public async Task GenerateKeyAsync_CreatesKeystore_AndRoundTripsPassword()
    {
        Environment.SetEnvironmentVariable("FDROID_MASTER_KEY", "test-master-key-0123456789abcdef");
        var svc = Create();

        var key = await svc.GenerateKeyAsync("org.example.test", null, TestContext.Current.CancellationToken);

        var full = Path.Combine(_keyDir, key.StorePath);
        File.Exists(full).Should().BeTrue();

        key.FingerprintSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        key.PasswordEncrypted.Should().NotBeEmpty();
        key.PasswordEncrypted.Should().NotContain("plaintext");

        var (fetched, password) = await svc.GetAvailableKeyAsync("org.example.test", null, TestContext.Current.CancellationToken);
        fetched.Id.Should().Be(key.Id);
        password.Should().NotBeNullOrEmpty();
        password.Length.Should().BeGreaterThanOrEqualTo(32);
    }

    [Fact]
    public async Task GenerateKeyAsync_PerBranch_CreatesSeparateKey()
    {
        Environment.SetEnvironmentVariable("FDROID_MASTER_KEY", "test-master-key-0123456789abcdef");
        var svc = Create();

        var k1 = await svc.GenerateKeyAsync("org.example.test", "main", TestContext.Current.CancellationToken);
        var k2 = await svc.GenerateKeyAsync("org.example.test", "develop", TestContext.Current.CancellationToken);

        k1.Branch.Should().Be("main");
        k2.Branch.Should().Be("develop");
        k1.StorePath.Should().NotBe(k2.StorePath);
        k1.FingerprintSha256.Should().NotBe(k2.FingerprintSha256);
    }

    [Fact]
    public async Task GetAvailableKeyAsync_NoKey_Throws()
    {
        var svc = Create();
        var act = async () => await svc.GetAvailableKeyAsync("org.example.test", null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No signing key*");
    }

    [Fact]
    public void GetFingerprint_InvalidFile_ReturnsUnknown()
    {
        KeyService.GetFingerprint("/nope/does-not-exist.keystore", "pass", "alias")
            .Should().Be("unknown");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FDROID_MASTER_KEY", null);
        try { Directory.Delete(_keyDir, true); } catch (IOException) { }
    }

    private KeyService Create()
    {
        var db = TestDb.Create();
        TestDb.SeedRepoConfigAsync(db, Path.GetTempPath(), _keyDir).GetAwaiter().GetResult();
        var protector = new PasswordProtector(db, new ConfigurationBuilder().Build(), new FakeEnvironmentService());
        return new KeyService(db, protector, TestIo.Real, new FakeEnvironmentService());
    }
}
