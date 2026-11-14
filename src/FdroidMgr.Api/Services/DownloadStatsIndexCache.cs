using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class DownloadStatsIndexCache : IDownloadStatsIndexCache
{
    private readonly Lock _lock = new();
    private string? _path;
    private DateTime _mtimeUtc;
    private IReadOnlyDictionary<string, (string Package, int VersionCode)>? _map;

    public IReadOnlyDictionary<string, (string Package, int VersionCode)>? Get(string path, DateTime mtimeUtc)
    {
        lock (_lock)
        {
            return _map is not null && _path == path && _mtimeUtc == mtimeUtc ? _map : null;
        }
    }

    public void Set(string path, DateTime mtimeUtc, IReadOnlyDictionary<string, (string Package, int VersionCode)> map)
    {
        lock (_lock)
        {
            if (_map is not null && _path == path && _mtimeUtc > mtimeUtc)
            {
                return;
            }

            _path = path;
            _mtimeUtc = mtimeUtc;
            _map = map;
        }
    }
}
