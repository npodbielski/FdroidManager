using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class RepoIndexServiceFailureTests : IDisposable
{
    private readonly string _missingRoot = Path.Combine(Path.GetTempPath(), "fdroidmgr-missing-" + Guid.NewGuid().ToString("N"), "repo");

    [Fact]
    public async Task GenerateAsync_MissingRepoRoot_RecordsStatusAndGivesUp()
    {
        var (db, runner, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

        runner.Log.Should().BeEmpty("there is nothing to run fdroid update against");
        var saved = await db.RepoConfig.AsNoTracking().FirstAsync();
        saved.LastIndexError.Should().Contain("fdroid repo directory not found");
        saved.LastIndexBuiltUtc.Should().BeNull();
    }

    [Fact]
    public async Task GenerateStrictAsync_MissingRepoRoot_ThrowsTheSameMessageItRecords()
    {
        var (db, _, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();

        var act = async () => await svc.GenerateStrictAsync(cfg, ct: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FdroidUpdateException>().WithMessage("*repo directory not found*");
        (await db.RepoConfig.AsNoTracking().FirstAsync()).LastIndexError.Should().Contain("repo directory not found");
    }

    [Fact]
    public async Task GenerateAsync_WithHeldLease_RunsWithoutTakingTheLockAgain()
    {
        var (db, runner, svc) = Create(createRepoRoot: true);
        var cfg = await db.RepoConfig.FirstAsync();
        var repoLock = new RepoLock();
        using var lease = await repoLock.AcquireAsync();

        await svc.GenerateAsync(cfg, lease: lease, ct: TestContext.Current.CancellationToken);

        runner.Log.Should().ContainSingle(i => i.Args.Contains("update"));
    }

    [Fact]
    public async Task GenerateAsync_StagingFailure_StillCountsAsASuccessfulBuild()
    {
        var broken = Substitute.For<IOService>();
        broken.DirectoryExists(Arg.Any<string>()).Returns(true);
        broken.EnumerateFilesRecursive(Arg.Any<string>()).Returns(_ => throw new IOException("mirror volume gone"));
        var stages = new RepoStageService(broken, TestConfig.Empty, new FakeEnvironmentService(), TestVisibility.Real, NullLogger<RepoStageService>.Instance);

        var (db, _, svc) = Create(createRepoRoot: true, stages: stages);
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

        (await db.RepoConfig.AsNoTracking().FirstAsync()).LastIndexError.Should().BeNull();
    }

    [Fact]
    public void ResolveRepoRoot_FallsBackToTheDefaultRoot()
    {
        var previous = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);
        try
        {
            var env = new FakeEnvironmentService();
            RepoIndexService.ResolveRepoRoot(new RepoConfig { RepoPath = "" }, env).Should().Be("/data/fdroidrepo");
            RepoIndexService.ResolveRepoRoot(new RepoConfig { RepoPath = "repo" }, env).Should().Be("/data/fdroidrepo");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", previous);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_missingRoot)!, true); } catch (IOException) { }
    }

    private (FdroidDb db, FakeCommandRunner runner, RepoIndexService svc) Create(
        bool createRepoRoot = false, RepoStageService? stages = null)
    {
        var db = TestDb.Create();
        TestDb.SeedRepoConfigAsync(db, _missingRoot, Path.GetTempPath()).GetAwaiter().GetResult();
        var runner = new FakeCommandRunner();
        var svc = new RepoIndexService(db, runner, NullLogger<RepoIndexService>.Instance, TestIo.Real, new RepoLock(), stages ?? TestStages.Create());
        if (createRepoRoot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_missingRoot)!);
        }

        return (db, runner, svc);
    }
}
