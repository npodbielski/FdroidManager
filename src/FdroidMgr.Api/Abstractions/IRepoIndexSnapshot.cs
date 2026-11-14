using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

/// <summary>
/// Per-request memo of the parsed repo index and its v2 icons, keyed by file
/// path and mtime. Endpoints read the index several times per request; the
/// mtime key keeps a mid-request rebuild (publish, metadata write) visible.
/// </summary>
public interface IRepoIndexSnapshot
{
    bool TryGetIndex(string path, DateTime mtimeUtc, out FdroidIndex? index);

    void SetIndex(string path, DateTime mtimeUtc, FdroidIndex? index);

    bool TryGetV2Icons(string path, DateTime mtimeUtc, out IReadOnlyDictionary<string, string>? icons);

    void SetV2Icons(string path, DateTime mtimeUtc, IReadOnlyDictionary<string, string> icons);
}
