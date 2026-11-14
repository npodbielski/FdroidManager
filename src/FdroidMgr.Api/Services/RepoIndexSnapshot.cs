using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

/// <summary>
/// Scoped <see cref="IRepoIndexSnapshot"/>: one instance per request, so no
/// locking is required and entries die with the request. Same idea as the
/// process-wide <see cref="DownloadStatsIndexCache"/>, only shorter-lived.
/// </summary>
public sealed class RepoIndexSnapshot : IRepoIndexSnapshot
{
    private (string Path, DateTime MtimeUtc, FdroidIndex? Index)? _index;

    private (string Path, DateTime MtimeUtc, IReadOnlyDictionary<string, string> Icons)? _icons;

    public bool TryGetIndex(string path, DateTime mtimeUtc, out FdroidIndex? index)
    {
        if (_index is { } hit && hit.Path == path && hit.MtimeUtc == mtimeUtc)
        {
            index = hit.Index;
            return true;
        }

        index = null;
        return false;
    }

    public void SetIndex(string path, DateTime mtimeUtc, FdroidIndex? index)
        => _index = (path, mtimeUtc, index);

    public bool TryGetV2Icons(string path, DateTime mtimeUtc, out IReadOnlyDictionary<string, string>? icons)
    {
        if (_icons is { } hit && hit.Path == path && hit.MtimeUtc == mtimeUtc)
        {
            icons = hit.Icons;
            return true;
        }

        icons = null;
        return false;
    }

    public void SetV2Icons(string path, DateTime mtimeUtc, IReadOnlyDictionary<string, string> icons)
        => _icons = (path, mtimeUtc, icons);
}
