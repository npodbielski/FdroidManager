namespace FdroidMgr.Api.Abstractions;

public interface IDownloadStatsIndexCache
{
    IReadOnlyDictionary<string, (string Package, int VersionCode)>? Get(string path, DateTime mtimeUtc);

    void Set(string path, DateTime mtimeUtc, IReadOnlyDictionary<string, (string Package, int VersionCode)> map);
}
