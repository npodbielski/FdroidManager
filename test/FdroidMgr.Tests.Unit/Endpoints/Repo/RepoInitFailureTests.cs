using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Endpoints.Repo;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class RepoInitFailureTests : IDisposable
{
    private const string MasterKey = "unit-test-master-key-0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fdroidmgr-initurl-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveRepoUrl_PublicUrlWins_ThenEnv_ThenStored()
    {
        Service(new FakeEnvironmentService { Vars = { ["FDROID_PUBLIC_URL"] = "https://apps.example.com/" } })
            .ResolveRepoUrl("http://stored/repo").Should().Be("https://apps.example.com/fdroid/repo");

        Service(new FakeEnvironmentService { Vars = { ["FDROID_REPO_URL"] = "http://env/repo" } })
            .ResolveRepoUrl("http://stored/repo").Should().Be("http://env/repo");

        Service(new FakeEnvironmentService())
            .ResolveRepoUrl("http://stored/repo").Should().Be("http://stored/repo");

        Service(new FakeEnvironmentService())
            .ResolveRepoUrl().Should().Be("http://localhost:5000/fdroid/repo");
    }

    [Fact]
    public async Task InitAsync_FdroidInitDoesNotCreateConfig_FailsLoudly()
    {
        Directory.CreateDirectory(_root);
        var svc = Create((_, _, workDir, _) =>
        {
            Directory.CreateDirectory(Path.Combine(workDir, "repo"));
            return new CommandResult(0, "init ok", "");
        });

        var act = async () => await svc.InitAsync(new RepoInitRequest { RepoName = "R" }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FdroidUpdateException>().WithMessage("*did not create config.yml*");
    }

    [Fact]
    public async Task InitAsync_UpdateAfterCreateFails_IsReported()
    {
        Directory.CreateDirectory(_root);
        var svc = Create((_, args, workDir, _) =>
        {
            if (args.StartsWith("init", StringComparison.Ordinal))
            {
                SeedInitOutput(workDir);
                return new CommandResult(0, "ok", "");
            }

            return args.StartsWith("update -c", StringComparison.Ordinal)
                ? new CommandResult(1, "", "mock: create failed")
                : new CommandResult(0, "ok", "");
        });

        var act = async () => await svc.InitAsync(new RepoInitRequest { RepoName = "R" }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FdroidUpdateException>().WithMessage("*fdroid update -c failed*");
    }

    [Fact]
    public async Task InitAsync_PlainUpdateFails_IsReported()
    {
        Directory.CreateDirectory(_root);
        var svc = Create((_, args, workDir, _) =>
        {
            if (args.StartsWith("init", StringComparison.Ordinal))
            {
                SeedInitOutput(workDir);
                return new CommandResult(0, "ok", "");
            }

            return args == "update"
                ? new CommandResult(1, "", "mock: update failed")
                : new CommandResult(0, "ok", "");
        });

        var act = async () => await svc.InitAsync(new RepoInitRequest { RepoName = "R" }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FdroidUpdateException>().WithMessage("*fdroid update failed*");
    }

    [Fact]
    public async Task InitAsync_RewritesExistingConfigKeys_AndAdoptsGivenKeystore()
    {
        Directory.CreateDirectory(_root);
        var keystore = Path.Combine(_root, "given.p12");
        File.WriteAllBytes(keystore, [0x30, 0x01]);

        var runner = NewRunner((_, args, workDir, _) =>
        {
            if (args.StartsWith("init", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(workDir);
                File.WriteAllText(Path.Combine(workDir, "config.yml"),
                    "repo_url: http://old/repo\nrepo_name: Old\nrepo_description: Old desc\nkeystorepass: 'sekret'\n");
                return new CommandResult(0, "ok", "");
            }

            Directory.CreateDirectory(Path.Combine(workDir, "repo"));
            return new CommandResult(0, "ok", "");
        });

        var svc = Create(TestDb.Create(), runner);
        await svc.InitAsync(new RepoInitRequest { RepoName = "New", RepoDescription = "New desc", KeystorePath = keystore }, TestContext.Current.CancellationToken);

        var config = await File.ReadAllTextAsync(Path.Combine(_root, "config.yml"));
        config.Should().Contain("repo_url: http://localhost:5000/fdroid/repo");
        config.Should().Contain("repo_name: New");
        config.Should().Contain("repo_description: New desc");
        runner.Log.Should().Contain(i => i.Args.Contains("--keystore " + keystore));
    }

    [Fact]
    public async Task InitAsync_WithoutAConfigRow_CreatesOneWithTheInitResults()
    {
        Directory.CreateDirectory(_root);
        var db = TestDb.Create();
        db.RepoConfig.RemoveRange(db.RepoConfig);
        await db.SaveChangesAsync();
        var svc = Create(db, NewRunner());

        await svc.InitAsync(new RepoInitRequest { RepoName = "Fresh" }, TestContext.Current.CancellationToken);

        var cfg = await db.RepoConfig.AsNoTracking().SingleAsync();
        cfg.RepoName.Should().Be("Fresh");
        cfg.RepoPath.Should().Be(Path.Combine(_root, "repo"));
        cfg.RepoKeystorePassEnc.Should().NotBeNullOrEmpty("fdroid reports the keystore password in config.yml");
        cfg.LastIndexBuiltUtc.Should().NotBeNull();
        cfg.LastIndexError.Should().BeNull();
    }

    [Fact]
    public async Task InitAsync_ForceClearsSubdirectoriesOfTheOldRepo()
    {
        Directory.CreateDirectory(Path.Combine(_root, "metadata", "org.old.app"));
        File.WriteAllText(Path.Combine(_root, "metadata", "org.old.app", "yml"), "Name: Old");
        File.WriteAllText(Path.Combine(_root, "config.yml"), "repo_url: http://old/repo\n");

        var svc = Create(NewRunner());
        var res = await svc.InitAsync(new RepoInitRequest { RepoName = "Fresh", Force = true }, TestContext.Current.CancellationToken);

        Directory.Exists(Path.Combine(_root, "metadata")).Should().BeFalse("a forced init starts from an empty repo root");
        Directory.Exists(res.BackupPath!).Should().BeTrue();
        File.Exists(Path.Combine(res.BackupPath!, "config.yml")).Should().BeTrue();
    }

    [Fact]
    public async Task InitAsync_ForceBacksUpNestedFilesBeforeClearingThem()
    {
        var metadataDir = Path.Combine(_root, "metadata", "org.old.app");
        Directory.CreateDirectory(metadataDir);
        File.WriteAllText(Path.Combine(metadataDir, "org.old.app_100.yml"), "AntiFeatures: []\n");
        File.WriteAllText(Path.Combine(_root, "config.yml"), "repo_url: http://old/repo\n");

        var svc = Create(NewRunner());
        var res = await svc.InitAsync(new RepoInitRequest { RepoName = "Fresh", Force = true }, TestContext.Current.CancellationToken);

        var backup = Path.Combine(res.BackupPath!, "metadata", "org.old.app", "org.old.app_100.yml");
        File.Exists(backup).Should().BeTrue("the backup must hold the whole repo tree, not only its top level");
        (await File.ReadAllTextAsync(backup)).Should().Contain("AntiFeatures");
        File.Exists(Path.Combine(metadataDir, "org.old.app_100.yml")).Should().BeFalse("the cleared root no longer serves the old metadata");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static void SeedInitOutput(string workDir)
    {
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "config.yml"), "repo_url: http://old/repo\nrepo_name: Old\nkeystorepass: 'sekret'\n");
    }

    private static FakeCommandRunner NewRunner(Func<string, string, string, CancellationToken, CommandResult>? handler = null)
    {
        var runner = new FakeCommandRunner();
        if (handler is not null)
        {
            runner.Handler = handler;
        }

        return runner;
    }

    private RepoInitService Create(Func<string, string, string, CancellationToken, CommandResult> handler)
        => Create(TestDb.Create(), NewRunner(handler));

    private RepoInitService Create(FakeCommandRunner runner)
        => Create(TestDb.Create(), runner);

    private RepoInitService Create(FdroidDb db, FakeCommandRunner runner)
    {
        var env = new FakeEnvironmentService { Vars = { ["FDROID_MASTER_KEY"] = MasterKey } };
        var protector = new PasswordProtector(db, new ConfigurationBuilder().Build(), env);

        return new RepoInitService(db, runner, NullLogger<RepoInitService>.Instance, TestIo.Real, env, protector)
        {
            RepoRoot = _root,
            AndroidHome = "/opt/android-sdk",
            MasterKey = MasterKey,
        };
    }

    private static RepoInitService Service(FakeEnvironmentService env)
    {
        var db = TestDb.Create();

        return new RepoInitService(db, new FakeCommandRunner(), NullLogger<RepoInitService>.Instance, TestIo.Real, env, new PasswordProtector(db, new ConfigurationBuilder().Build(), env))
        {
            RepoRoot = "/nonexistent-root-for-this-test",
        };
    }
}
