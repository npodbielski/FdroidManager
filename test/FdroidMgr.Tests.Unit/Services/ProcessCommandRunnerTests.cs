using FdroidMgr.Api.Services;
using FdroidMgr.Tests.Unit.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FdroidMgr.Tests.Unit.Services;

public class ProcessCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsExitCodeAndOutput_OnSuccess()
    {
        var runner = Create();

        var res = await runner.RunAsync("/bin/sh", ["-c", "echo hello; echo oops >&2; exit 3"], "/", TestContext.Current.CancellationToken);

        res.ExitCode.Should().Be(3);
        res.Stdout.Trim().Should().Be("hello");
        res.Stderr.Trim().Should().Be("oops");
    }

    [Fact]
    public async Task RunAsync_KillsProcess_AndReturns124_OnTimeout()
    {
        var runner = Create(timeoutSeconds: "1");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await runner.RunAsync("/bin/sleep", ["60"], "/", TestContext.Current.CancellationToken);
        sw.Stop();

        res.ExitCode.Should().Be(ProcessCommandRunner.TimeoutExitCode);
        res.Stderr.Should().Be("timed out");
        res.Stdout.Should().BeEmpty();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the hung process must be killed at the timeout, not run to completion");
    }

    [Fact]
    public async Task RunAsync_CapturesLargeStdoutAndStderr_WithoutDeadlock()
    {
        var runner = Create(timeoutSeconds: "60");
        const string script = "i=0; while [ $i -lt 4000 ]; do echo 0123456789012345678901234567890123456789012345678901234567890123456789; echo 0123456789012345678901234567890123456789012345678901234567890123456789 >&2; i=$((i+1)); done";

        var res = await runner.RunAsync("/bin/sh", ["-c", script], "/", TestContext.Current.CancellationToken);

        res.Success.Should().BeTrue();
        res.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(4000);
        res.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(4000);
    }

    [Fact]
    public async Task RunAsync_ThrowsAndKills_WhenCallerCancels()
    {
        var runner = Create(timeoutSeconds: "60");
        using var cts = new CancellationTokenSource();

        var task = runner.RunAsync("/bin/sleep", ["60"], "/", cts.Token);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        cts.Cancel();

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("fdroid", SettingsService.FdroidTimeoutSeconds)]
    [InlineData("/usr/bin/fdroid", SettingsService.FdroidTimeoutSeconds)]
    [InlineData("keytool", SettingsService.ShortTimeoutSeconds)]
    [InlineData("apksigner", SettingsService.ShortTimeoutSeconds)]
    [InlineData("aapt", SettingsService.ShortTimeoutSeconds)]
    public void ProcessTimeoutSeconds_DefaultsPerExe(string exe, int expected)
    {
        var env = new FakeEnvironmentService { Vars = { ["FDROID_PROCESS_TIMEOUT_SECONDS"] = null } };
        new SettingsService(env).ProcessTimeoutSeconds(exe).Should().Be(expected);
    }

    [Fact]
    public void ProcessTimeoutSeconds_EnvOverridesBothDefaults()
    {
        var env = new FakeEnvironmentService { Vars = { ["FDROID_PROCESS_TIMEOUT_SECONDS"] = "5" } };
        var settings = new SettingsService(env);

        settings.ProcessTimeoutSeconds("fdroid").Should().Be(5);
        settings.ProcessTimeoutSeconds("keytool").Should().Be(5);
    }

    private static ProcessCommandRunner Create(string? timeoutSeconds = null)
    {
        var env = new FakeEnvironmentService();
        if (timeoutSeconds is not null)
        {
            env.Vars["FDROID_PROCESS_TIMEOUT_SECONDS"] = timeoutSeconds;
        }

        var settings = new SettingsService(env);
        return new ProcessCommandRunner(NullLogger<ProcessCommandRunner>.Instance, settings);
    }
}
