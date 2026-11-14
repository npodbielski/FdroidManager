using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Tests.Unit.Fixtures;

public sealed class FakeCommandRunner : IRepoCommandRunner
{
    public readonly record struct Invocation(string Exe, string Args, string WorkDir);

    public List<Invocation> Log { get; } = [];

    public Func<string, string, string, CancellationToken, CommandResult>? Handler { get; set; }

    public Func<string, string, string, CancellationToken, Task<CommandResult>>? AsyncHandler { get; set; }

    public bool SimulateInitFailure { get; set; }
    public bool SimulateUpdateFailure { get; set; }

    public async Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, string workingDir, CancellationToken ct = default)
    {
        var joined = string.Join(" ", args);
        Log.Add(new Invocation(exe, joined, workingDir));

        if (AsyncHandler is not null)
        {
            return await AsyncHandler(exe, joined, workingDir, ct);
        }

        if (Handler is not null)
        {
            return Handler(exe, joined, workingDir, ct);
        }

        if (exe == "fdroid" && args.Count > 0 && args[0] == "init")
        {
            if (SimulateInitFailure)
            {
                return new CommandResult(1, "", "mock: fdroid init failed");
            }

            Directory.CreateDirectory(workingDir);
            if (!File.Exists(Path.Combine(workingDir, "config.yml")))
            {
                File.WriteAllText(Path.Combine(workingDir, "config.yml"),
                    "sdk_path: /opt/android-sdk\nrepo_keyalias: stub\nkeystore: keystore.p12\nkeystorepass: c3R1Yi1wYXNz\nkeypass: c3R1Yi1wYXNz\n");
            }

            File.WriteAllBytes(Path.Combine(workingDir, "keystore.p12"), [0x30, 0x82]);
            return new CommandResult(0, "init ok", "");
        }

        if (exe == "fdroid" && args.Count > 0 && args[0] == "update")
        {
            if (SimulateUpdateFailure)
            {
                return new CommandResult(1, "", "mock: fdroid update failed");
            }

            Directory.CreateDirectory(workingDir);
            Directory.CreateDirectory(Path.Combine(workingDir, "repo"));
            File.WriteAllText(Path.Combine(workingDir, "repo", "index.xml"), "<fdroid>stub</fdroid>");
            return new CommandResult(0, "update ok", "");
        }

        return new CommandResult(0, "", "");
    }
}
