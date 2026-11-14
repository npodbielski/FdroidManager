using System.Threading.Channels;

namespace FdroidMgr.Api.Abstractions;

/// <summary>
/// Decouples an APK download from its stats write: the download endpoint only
/// enqueues, a single hosted consumer drains the queue into the database. A
/// full queue drops the newest hit — stats must never make a download wait.
/// </summary>
public interface IDownloadStatsQueue
{
    bool TryEnqueue(string package, int versionCode);

    ChannelReader<(string Package, int VersionCode)> Reader { get; }

    int Dropped { get; }
}
