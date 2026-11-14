using System.Threading.Channels;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class DownloadStatsQueue(ILogger<DownloadStatsQueue> log) : IDownloadStatsQueue
{
    private const int Capacity = 1024;

    private readonly Channel<(string Package, int VersionCode)> _channel =
        Channel.CreateBounded<(string, int)>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private int _dropped;

    public int Dropped => Volatile.Read(ref _dropped);

    public ChannelReader<(string Package, int VersionCode)> Reader => _channel.Reader;

    public bool TryEnqueue(string package, int versionCode)
    {
        if (_channel.Writer.TryWrite((package, versionCode)))
        {
            return true;
        }

        var dropped = Interlocked.Increment(ref _dropped);
        Log.HitDropped(log, package, versionCode, dropped);
        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 42, Level = LogLevel.Warning,
            Message = "download stats queue is full, dropped a hit for {Package} v{Version} ({Dropped} so far)")]
        public static partial void HitDropped(ILogger logger, string package, int version, int dropped);
    }
}
