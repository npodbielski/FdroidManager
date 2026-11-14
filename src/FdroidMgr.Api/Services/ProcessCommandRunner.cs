using System.Diagnostics;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed partial class ProcessCommandRunner(ILogger<ProcessCommandRunner> log, ISettingsService settings)
    : IRepoCommandRunner
{
    public const int TimeoutExitCode = 124;

    public async Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, string workingDir,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        Log.ProcessRunning(log, exe, workingDir, string.Join(" ", args));
        using var p = Process.Start(psi)
                      ?? throw new InvalidOperationException($"failed to start {exe}");

        var timeoutSeconds = settings.ProcessTimeoutSeconds(exe);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var stdout = p.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = p.StandardError.ReadToEndAsync(linked.Token);
        var exit = p.WaitForExitAsync(linked.Token);

        try
        {
            await Task.WhenAll(stdout, stderr, exit);
        }
        catch (OperationCanceledException)
        {
            KillTree(p);
            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                Log.ProcessTimedOut(log, exe, workingDir, timeoutSeconds);
                return new CommandResult(TimeoutExitCode, "", "timed out");
            }

            throw;
        }

        return new CommandResult(p.ExitCode, await stdout, await stderr);
    }

    private void KillTree(Process p)
    {
        try
        {
            p.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log.ProcessKillFailed(log, p.ProcessName, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 34, Level = LogLevel.Debug,
            Message = "Running {Exe} in {WorkDir} with args: {Args}")]
        public static partial void ProcessRunning(ILogger logger, string exe, string workDir, string args);

        [LoggerMessage(EventId = 35, Level = LogLevel.Warning,
            Message = "{Exe} in {WorkDir} timed out after {TimeoutSeconds}s and was killed")]
        public static partial void ProcessTimedOut(ILogger logger, string exe, string workDir, int timeoutSeconds);

        [LoggerMessage(EventId = 36, Level = LogLevel.Warning,
            Message = "failed to kill process tree for {Exe}")]
        public static partial void ProcessKillFailed(ILogger logger, string exe, Exception ex);
    }
}
