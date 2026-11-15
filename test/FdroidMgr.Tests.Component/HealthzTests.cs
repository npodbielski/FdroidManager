using System.Net;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Api.Services;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component;

public class HealthzTests
{
    [Fact]
    public async Task Healthz_IsAnonymous_AndBootCompletesWithoutRebuild()
    {
        // The repo root is pinned per host: FDROID_REPO_ROOT is process-wide,
        // and a parallel collection must not see it.
        using var host = new TestHost(new TestHostOptions
        {
            DbName = "healthz.db",
            DirPrefix = "fdroidmgr-healthz",
            EnvVars = new Dictionary<string, string?> { ["FDROID_REPO_ROOT"] = null },
        });
        var client = host.Client();

        var res = await client.GetAsync("/healthz");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("ok");

        await WaitUntil(async () =>
        {
            var b = JsonDocument.Parse(await (await client.GetAsync("/healthz")).Content.ReadAsStringAsync()).RootElement;
            return b.GetProperty("bootCompleted").GetBoolean();
        });
    }

    [Fact]
    public async Task Healthz_ServesWhileBootRebuildIsBlocked()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dir = Path.Combine(Path.GetTempPath(), $"fdroidmgr-healthz-boot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        using var host = new TestHost(new TestHostOptions
        {
            DbName = "healthz-boot.db",
            DirPrefix = "fdroidmgr-healthz-boot",
            EnvVars = new Dictionary<string, string?> { ["FDROID_REPO_ROOT"] = dir },
            Runner = r => r.AsyncHandler = async (exe, args, _, _) =>
            {
                if (exe == "fdroid" && args.Contains("update"))
                {
                    await gate.Task;
                }

                return new CommandResult(0, "ok", "");
            },
        });
        File.WriteAllText(Path.Combine(dir, "config.yml"), "sdk_path: /opt/android-sdk\n");

        var client = host.Client();
        var res = await client.GetAsync("/healthz");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var blocked = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        blocked.GetProperty("bootCompleted").GetBoolean().Should().BeFalse("the health endpoint must serve while the boot rebuild is still running");

        gate.SetResult();
        await WaitUntil(async () =>
        {
            var b = JsonDocument.Parse(await (await client.GetAsync("/healthz")).Content.ReadAsStringAsync()).RootElement;
            return b.GetProperty("bootCompleted").GetBoolean();
        });

        host.Runner.Log.Should().Contain(i => i.Exe == "fdroid" && i.Args.Contains("update"));
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("condition not met");
    }
}
