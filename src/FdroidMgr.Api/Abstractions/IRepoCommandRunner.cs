using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

public interface IRepoCommandRunner
{
    Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, string workingDir, CancellationToken ct = default);
}
