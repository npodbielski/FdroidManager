using FdroidMgr.Api.Data;

namespace FdroidMgr.Api.Abstractions;

public interface IDatabaseInitializer
{
    Task InitializeAsync(FdroidDb db, Func<string, string, string> resolve, IEnvironmentService env, CancellationToken ct = default);

    Task PinPathsAsync(FdroidDb db, Func<string, string?> envOrConfig, CancellationToken ct = default);
}
