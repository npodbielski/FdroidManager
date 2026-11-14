using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api;

/// <summary>
/// Single consumer of <see cref="IDownloadStatsQueue"/>: drains download hits
/// into the database outside the request that produced them. One consumer also
/// keeps the SQLite writes serialised.
/// </summary>
public partial class DownloadStatsWriter(
    IDownloadStatsQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DownloadStatsWriter> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (package, versionCode) in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IDownloadStatsService>()
                        .RecordAsync(package, versionCode, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.DrainFailed(log, ex, package, versionCode);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: the reader completed because the host stopped.
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 41, Level = LogLevel.Warning,
            Message = "could not record queued download stat for {Package} v{Version}")]
        public static partial void DrainFailed(ILogger logger, Exception ex, string package, int version);
    }
}
