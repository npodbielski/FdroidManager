using FdroidMgr.Api.Data;
using System.Text.RegularExpressions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class RepoIndexService(
    FdroidDb db,
    IRepoCommandRunner runner,
    ILogger<RepoIndexService> log,
    IOService io,
    IRepoLock repoLock,
    IRepoStageService stages)
    : IRepoIndexService
{

    private static List<string> BuildArgs(bool createMetadata)
    {
        var args = new List<string> { "update", "--rename-apks" };
        if (createMetadata)
        {
            args.Add("--create-metadata");
        }

        return args;
    }

    public virtual async Task GenerateAsync(RepoConfig cfg, bool createMetadata = false, IRepoLockLease? lease = null, CancellationToken ct = default)
    {
        if (lease is not null)
        {
            await GenerateCoreAsync(cfg, createMetadata, ct);
            return;
        }

        using var held = await repoLock.AcquireAsync(ct);
        await GenerateCoreAsync(cfg, createMetadata, ct);
    }

    private async Task GenerateCoreAsync(RepoConfig cfg, bool createMetadata, CancellationToken ct)
    {
        var repoRoot = ResolveRepoRoot(cfg);
        if (!io.DirectoryExists(repoRoot))
        {
            Log.RepoDirNotFound(log, repoRoot);
            await RecordOutcomeAsync(success: false, $"fdroid repo directory not found at {repoRoot}", ct);
            return;
        }

        Log.FdroidUpdateRunning(log, repoRoot);
        var res = await runner.RunAsync("fdroid", BuildArgs(createMetadata), repoRoot, ct);
        if (res.Success)
        {
            Log.FdroidUpdateSucceeded(log);
            StageQuietly(cfg);
            await RecordOutcomeAsync(success: true, error: null, ct);
        }
        else
        {
            Log.FdroidUpdateFailed(log, res.ExitCode, res.Stderr);
            await RecordOutcomeAsync(success: false, Trim(res.Stderr), ct);
        }
    }

    public virtual async Task GenerateStrictAsync(RepoConfig cfg, bool createMetadata = false,
        IRepoLockLease? lease = null, CancellationToken ct = default)
    {
        if (lease is not null)
        {
            await GenerateStrictCoreAsync(cfg, createMetadata, ct);
            return;
        }

        using var held = await repoLock.AcquireAsync(ct);
        await GenerateStrictCoreAsync(cfg, createMetadata, ct);
    }

    private async Task GenerateStrictCoreAsync(RepoConfig cfg, bool createMetadata, CancellationToken ct)
    {
        var repoRoot = ResolveRepoRoot(cfg);
        if (!io.DirectoryExists(repoRoot))
        {
            var message = $"fdroid update failed: repo directory not found at {repoRoot}";
            await RecordOutcomeAsync(success: false, message, ct);
            throw new FdroidUpdateException(message);
        }

        var res = await runner.RunAsync("fdroid", BuildArgs(createMetadata), repoRoot, ct);
        if (!res.Success)
        {
            var message = $"fdroid update failed: {Trim(res.Stderr)}";
            await RecordOutcomeAsync(success: false, message, ct);
            throw new FdroidUpdateException(message);
        }

        StageQuietly(cfg);
        await RecordOutcomeAsync(success: true, error: null, ct);
    }

    private void StageQuietly(RepoConfig cfg)
    {
        try
        {
            stages.Stage(cfg);
        }
        catch (Exception ex)
        {
            Log.RepoStageFailed(log, ex);
        }
    }

    private static string Trim(string? s) => MyRegex().Replace(s ?? "", " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MyRegex();

    private static partial class Log
    {
        [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
            Message = "fdroid repo directory not found at {RepoRoot}")]
        public static partial void RepoDirNotFound(ILogger logger, string repoRoot);

        [LoggerMessage(EventId = 13, Level = LogLevel.Information,
            Message = "Running fdroid update in {RepoRoot}")]
        public static partial void FdroidUpdateRunning(ILogger logger, string repoRoot);

        [LoggerMessage(EventId = 14, Level = LogLevel.Information,
            Message = "fdroid update succeeded")]
        public static partial void FdroidUpdateSucceeded(ILogger logger);

        [LoggerMessage(EventId = 15, Level = LogLevel.Warning,
            Message = "fdroid update failed with exit code {ExitCode}: {Error}")]
        public static partial void FdroidUpdateFailed(ILogger logger, int exitCode, string error);

        [LoggerMessage(EventId = 16, Level = LogLevel.Warning,
            Message = "failed to record index build outcome")]
        public static partial void IndexOutcomeRecordFailed(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 17, Level = LogLevel.Warning,
            Message = "fdroid update succeeded but publishing the served mirror failed")]
        public static partial void RepoStageFailed(ILogger logger, Exception ex);
    }
}
