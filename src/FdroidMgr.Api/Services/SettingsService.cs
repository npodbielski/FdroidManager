using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class SettingsService(IEnvironmentService env) : ISettingsService
{
    public const int FdroidTimeoutSeconds = 900;
    public const int ShortTimeoutSeconds = 60;

    public int ProcessTimeoutSeconds(string exe) =>
        int.TryParse(env.GetEnvironmentVariable("FDROID_PROCESS_TIMEOUT_SECONDS"), out var seconds) && seconds > 0
            ? seconds
            : Path.GetFileName(exe).Contains("fdroid", StringComparison.Ordinal)
                ? FdroidTimeoutSeconds
                : ShortTimeoutSeconds;
}
