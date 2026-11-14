using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class RepoInitServiceTests : IDisposable
{
    private readonly string _repoRoot = Path.Combine(Path.GetTempPath(), "fdroidmgr-init-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validate_RequiresRepoName()
    {
        var req = Valid() with { RepoName = "" };
        var act = () => RepoInitService.Validate(req);
        act.Should().Throw<ArgumentException>().WithMessage("repoName is required");
    }

    [Fact]
    public void Validate_RejectsBlankKeyAlias()
    {
        var req = Valid() with { KeyAlias = " " };
        var act = () => RepoInitService.Validate(req);
        act.Should().Throw<ArgumentException>().WithMessage("keyAlias is invalid");
    }

    [Fact]
    public async Task InitAsync_FreshRepo_CreatesConfigAndRunsUpdate()
    {
        var (_, runner, svc) = Create();

        var res = await svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        res.RepoUrl.Should().Be("http://localhost:5000/fdroid/repo");
        res.RepoName.Should().Be("Test Repo");
        res.RepoPath.Should().Be(Path.Combine(_repoRoot, "repo"));
        File.Exists(Path.Combine(_repoRoot, "config.yml")).Should().BeTrue();
        File.Exists(Path.Combine(_repoRoot, "keystore.p12")).Should().BeTrue();
        runner.Log.Should().Contain(i => i.Exe == "fdroid" && i.Args == "init --no-prompt --android-home /opt/android-sdk --repo-keyalias fdroid-repo");
        runner.Log.Should().Contain(i => i.Args == "update -c");
        runner.Log.Should().Contain(i => i.Args == "update");
    }

    [Fact]
    public async Task InitAsync_ExistingRepo_Throws409_WithoutForce_AndBacksUp()
    {
        Directory.CreateDirectory(_repoRoot);
        File.WriteAllText(Path.Combine(_repoRoot, "config.yml"), "repo_url: http://old/repo\n");
        File.WriteAllBytes(Path.Combine(_repoRoot, "keystore.p12"), [0x01]);
        var (_, _, svc) = Create();

        var act = () => svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<RepoAlreadyInitializedException>();
        ex.Which.BackupPath.Should().Contain("-backup-");

        Directory.Exists(ex.Which.BackupPath).Should().BeTrue();
        File.Exists(Path.Combine(ex.Which.BackupPath, "config.yml")).Should().BeTrue();
        File.Exists(Path.Combine(ex.Which.BackupPath, "keystore.p12")).Should().BeTrue();

        File.Exists(Path.Combine(_repoRoot, "config.yml")).Should().BeTrue();

        var act2 = () => svc.InitAsync(Valid(), TestContext.Current.CancellationToken);
        await act2.Should().ThrowAsync<RepoAlreadyInitializedException>();

        var backups = Directory.GetParent(_repoRoot)!.GetDirectories()
            .Where(d => d.Name.StartsWith(Path.GetFileName(_repoRoot) + "-backup-")).ToList();
        backups.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InitAsync_ExistingRepo_ForceCreatesBackupThenFreshInit()
    {
        Directory.CreateDirectory(_repoRoot);
        File.WriteAllText(Path.Combine(_repoRoot, "config.yml"), "repo_url: http://old/repo\n");
        File.WriteAllBytes(Path.Combine(_repoRoot, "keystore.p12"), [0x01]);
        var (_, _, svc) = Create();

        var res = await svc.InitAsync(Valid() with { Force = true }, TestContext.Current.CancellationToken);

        res.RepoUrl.Should().Be("http://localhost:5000/fdroid/repo");
        res.RepoName.Should().Be("Test Repo");
        res.BackupPath.Should().NotBeNull();
        Directory.Exists(res.BackupPath).Should().BeTrue();
        File.Exists(Path.Combine(res.BackupPath!, "config.yml")).Should().BeTrue();
        File.Exists(Path.Combine(res.BackupPath!, "keystore.p12")).Should().BeTrue();

        File.Exists(Path.Combine(_repoRoot, "config.yml")).Should().BeTrue();
    }

    [Fact]
    public async Task InitAsync_FdroidInitFailure_Throws()
    {
        var (_, runner, svc) = Create();
        runner.SimulateInitFailure = true;

        var act = () => svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<FdroidUpdateException>())
            .WithMessage("*fdroid init failed*");
    }

    [Fact]
    public async Task InitAsync_UpdateFailure_Throws()
    {
        var (_, runner, svc) = Create();
        runner.SimulateUpdateFailure = true;

        var act = () => svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<FdroidUpdateException>();
        ex.WithMessage("*fdroid update*");
    }

    [Fact]
    public async Task InitAsync_StoresEncryptedPasswordAndUpsertsRepoConfig()
    {

        Directory.CreateDirectory(_repoRoot);
        var (db, _, svc) = Create();

        await TestDb.SeedRepoConfigAsync(db, Path.Combine(_repoRoot, "repo"), Path.Combine(_repoRoot, "keys"));

        await svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        var cfg = await db.RepoConfig.FirstAsync();
        cfg.RepoUrl.Should().Be("http://localhost:5000/fdroid/repo");
        cfg.RepoName.Should().Be("Test Repo");
        cfg.RepoKeystorePassEnc.Should().NotBeNullOrEmpty();
        cfg.RepoKeystorePassEnc.Should().Contain(":");
        if (!string.IsNullOrEmpty(cfg.RepoKeySha256))
        {
            cfg.RepoKeySha256.Should().MatchRegex("^[0-9a-f]{64}$");
        }
    }

    [Fact]
    public async Task InitAsync_PatchesConfigUrlNameDescription()
    {
        var (_, _, svc) = Create();

        await svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        var config = File.ReadAllText(Path.Combine(_repoRoot, "config.yml"));
        config.Should().Contain("repo_url: http://localhost:5000/fdroid/repo");
        config.Should().Contain("repo_name: Test Repo");
        config.Should().Contain("repo_description: desc");
    }

    [Fact]
    public void DefaultKeyAlias_IsSensibleConstant()
    {
        RepoInitService.DefaultKeyAlias().Should().Be("fdroid-repo");
    }

    [Fact]
    public async Task InitAsync_WithoutAlias_DefaultsToFdroidRepoAndPassesItToFdroid()
    {
        var (_, runner, svc) = Create();

        var res = await svc.InitAsync(Valid(), TestContext.Current.CancellationToken);

        res.KeyAlias.Should().Be("fdroid-repo");
        runner.Log.Should().Contain(i => i.Exe == "fdroid" && i.Args.Contains("--repo-keyalias fdroid-repo"));
    }

    [Fact]
    public void IsInitialized_TrueWhenConfigExists_ElseFalse()
    {
        var (_, _, svc) = Create();
        svc.IsInitialized().Should().BeFalse();

        Directory.CreateDirectory(_repoRoot);
        File.WriteAllText(Path.Combine(_repoRoot, "config.yml"), "x: 1");
        svc.IsInitialized().Should().BeTrue();
    }

    public void Dispose()
    {
        try { Directory.Delete(_repoRoot, true); } catch (IOException) { }
        var backups = Directory.GetParent(_repoRoot)?.GetDirectories()
            .Where(d => d.Name.StartsWith(Path.GetFileName(_repoRoot) + "-backup-"));
        if (backups is not null)
        {
            foreach (var b in backups)
            {
                try { Directory.Delete(b.FullName, true); } catch (IOException) { }
            }
        }
    }

    private (FdroidDb db, FakeCommandRunner runner, RepoInitService svc) Create()
    {
        var db = TestDb.Create();
        var runner = new FakeCommandRunner();
        var env = new FakeEnvironmentService { Vars = { ["FDROID_MASTER_KEY"] = "unit-test-master-key-0123456789abcdef" } };
        var protector = new PasswordProtector(db, new ConfigurationBuilder().Build(), env);
        var svc = new RepoInitService(db, runner, NullLogger<RepoInitService>.Instance, TestIo.Real, env, protector)
        {
            RepoRoot = _repoRoot,
            AndroidHome = "/opt/android-sdk",
            MasterKey = "unit-test-master-key-0123456789abcdef",
        };
        return (db, runner, svc);
    }

    private static RepoInitRequest Valid() => new()
    {
        RepoName = "Test Repo",
        RepoDescription = "desc",
    };
}
