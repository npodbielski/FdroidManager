using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class RepoIndexServiceGenerateTests : IDisposable
{
    private readonly string _repoDir;

    public RepoIndexServiceGenerateTests()
    {
        _repoDir = Path.Combine(Path.GetTempPath(), "fdroidmgr-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoDir);
    }

    [Fact]
    public async Task GenerateAsync_RunsFdroidUpdateWithRenameApks()
    {
        var (db, runner, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

        runner.Log.Should().Contain(i => i.Exe == "fdroid" && i.Args.Contains("update") && i.Args.Contains("--rename-apks"));
    }

    [Fact]
    public async Task GenerateStrictAsync_PassesRenameApks_AndThrowsOnFailure()
    {
        var (db, runner, svc) = Create();
        runner.SimulateUpdateFailure = true;
        var cfg = await db.RepoConfig.FirstAsync();

        var act = async () => await svc.GenerateStrictAsync(cfg, ct: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<FdroidUpdateException>().WithMessage("*fdroid update failed*");
        runner.Log.Should().Contain(i => i.Exe == "fdroid" && i.Args.Contains("--rename-apks"));
    }

    [Fact]
    public async Task GenerateStrictAsync_AddsCreateMetadata_WhenRequested()
    {
        var (db, runner, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateStrictAsync(cfg, createMetadata: true, ct: TestContext.Current.CancellationToken);

        runner.Log.Last(i => i.Exe == "fdroid").Args.Should().Contain("--create-metadata");
    }

    [Fact]
    public async Task GenerateAsync_LogsSuccess_DoesNotThrow_WhenFdroidSucceeds()
    {
        var (db, _, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

    }

    [Fact]
    public async Task GenerateAsync_LogsWarning_WhenFdroidFails()
    {
        var (db, runner, svc) = Create();
        runner.SimulateUpdateFailure = true;
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

    }

    [Fact]
    public async Task GenerateAsync_Success_RecordsBuildTimeAndClearsError()
    {
        var (db, _, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();
        cfg.LastIndexError = "previous failure";
        await db.SaveChangesAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

        var row = await db.RepoConfig.FirstAsync();
        row.LastIndexBuiltUtc.Should().NotBeNull();
        row.LastIndexError.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_Failure_RecordsError_AndLeavesBuildTimeUnchanged()
    {
        var (db, runner, svc) = Create();
        runner.SimulateUpdateFailure = true;
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateAsync(cfg, ct: TestContext.Current.CancellationToken);

        var row = await db.RepoConfig.FirstAsync();
        row.LastIndexError.Should().Be("mock: fdroid update failed");
        row.LastIndexBuiltUtc.Should().BeNull();
    }

    [Fact]
    public async Task GenerateStrictAsync_Failure_RecordsError_AndThrows()
    {
        var (db, runner, svc) = Create();
        runner.SimulateUpdateFailure = true;
        var cfg = await db.RepoConfig.FirstAsync();

        var act = async () => await svc.GenerateStrictAsync(cfg, ct: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<FdroidUpdateException>();

        (await db.RepoConfig.FirstAsync()).LastIndexError.Should().Be("fdroid update failed: mock: fdroid update failed");
    }

    [Fact]
    public async Task GenerateStrictAsync_Success_RecordsBuildTime()
    {
        var (db, _, svc) = Create();
        var cfg = await db.RepoConfig.FirstAsync();

        await svc.GenerateStrictAsync(cfg, ct: TestContext.Current.CancellationToken);

        var row = await db.RepoConfig.FirstAsync();
        row.LastIndexBuiltUtc.Should().NotBeNull();
        row.LastIndexError.Should().BeNull();
    }

    [Fact]
    public void GenerateAsync_ResolvesRepoRootFromEnv_WhenSet()
    {
        Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", "/tmp/fake-fdroid-root");
        try
        {
            var cfg = new RepoConfig { RepoPath = "/data/fdroidrepo/repo" };

            Directory.CreateDirectory("/tmp/fake-fdroid-root");
            RepoIndexServiceTestsPlaceholder.Resolve(cfg).Should().Be("/tmp/fake-fdroid-root");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);
            try { Directory.Delete("/tmp/fake-fdroid-root", true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GenerateStrictAsync_ConcurrentRuns_SerializeBehindRepoLock()
    {
        var repoLock = new RepoLock();
        var runner = new FakeCommandRunner();
        var dbA = TestDb.Create();
        var dbB = TestDb.Create();
        await TestDb.SeedRepoConfigAsync(dbA, _repoDir, Path.GetTempPath());
        await TestDb.SeedRepoConfigAsync(dbB, _repoDir, Path.GetTempPath());
        var cfg = await dbA.RepoConfig.FirstAsync();
        var svcA = new RepoIndexService(dbA, runner, NullLogger<RepoIndexService>.Instance, TestIo.Real, repoLock, TestStages.Create());
        var svcB = new RepoIndexService(dbB, runner, NullLogger<RepoIndexService>.Instance, TestIo.Real, repoLock, TestStages.Create());

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ConcurrencyProbe();
        runner.AsyncHandler = async (_, _, _, _) =>
        {
            probe.Enter();
            await gate.Task;
            probe.Exit();
            return new CommandResult(0, "update ok", "");
        };

        var ct = TestContext.Current.CancellationToken;
        var first = svcA.GenerateStrictAsync(cfg, ct: ct);
        var second = svcB.GenerateStrictAsync(cfg, ct: ct);

        while (probe.Current == 0)
        {
            await Task.Delay(10, ct);
        }

        await Task.Delay(100, ct);
        runner.Log.Should().HaveCount(1, "the second fdroid update must wait for the first lease to be released");

        gate.SetResult();
        await Task.WhenAll(first, second);

        probe.Max.Should().Be(1);

        runner.Log.Should().HaveCount(2);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, true); } catch (IOException) { }
    }

    private (FdroidDb db, FakeCommandRunner runner, RepoIndexService svc) Create(bool createRepoRoot = true)
    {
        var db = TestDb.Create();
        TestDb.SeedRepoConfigAsync(db, _repoDir, Path.GetTempPath()).GetAwaiter().GetResult();
        var runner = new FakeCommandRunner();
        var svc = new RepoIndexService(db, runner, NullLogger<RepoIndexService>.Instance, TestIo.Real, new RepoLock(), TestStages.Create());
        if (createRepoRoot)
        {
            Directory.CreateDirectory(Path.Combine(_repoDir, "..", Path.GetFileName(Path.GetDirectoryName(_repoDir))!));
        }

        return (db, runner, svc);
    }
}

file sealed class ConcurrencyProbe
{
    private int _active;
    private int _max;

    public int Current => Volatile.Read(ref _active);

    public int Max => Volatile.Read(ref _max);

    public void Enter()
    {
        var now = Interlocked.Increment(ref _active);
        var prev = Volatile.Read(ref _max);
        while (now > prev && Interlocked.CompareExchange(ref _max, now, prev) != prev)
        {
            prev = Volatile.Read(ref _max);
        }
    }

    public void Exit() => Interlocked.Decrement(ref _active);
}

public static class RepoIndexServiceTestsPlaceholder
{
    public static string Resolve(RepoConfig cfg)
    {

        var env = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        if (!string.IsNullOrWhiteSpace(cfg.RepoPath))
        {
            var parent = Path.GetDirectoryName(cfg.RepoPath.TrimEnd(Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(parent))
            {
                return parent;
            }
        }
        return "/data/fdroidrepo";
    }
}
