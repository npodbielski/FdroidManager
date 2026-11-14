using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Services;

public partial class FdroidRepoService
{
    public virtual async Task<string?> ReadChangelogAsync(RepoConfig cfg, string packageName, int versionCode, CancellationToken ct = default)
    {
        var path = ChangelogPath(RepoIndexService.ResolveRepoRoot(cfg), packageName, versionCode);
        return io.FileExists(path) ? await io.ReadAllTextAsync(path, ct) : null;
    }

    public virtual string? ReadChangelog(RepoConfig cfg, string packageName, int versionCode)
    {
        var path = ChangelogPath(RepoIndexService.ResolveRepoRoot(cfg), packageName, versionCode);
        return io.FileExists(path) ? io.ReadAllText(path) : null;
    }

    public virtual async Task WriteChangelogAsync(RepoConfig cfg, string packageName, int versionCode, string changelog, CancellationToken ct = default)
    {
        var path = ChangelogPath(RepoIndexService.ResolveRepoRoot(cfg), packageName, versionCode);
        var dir = Path.GetDirectoryName(path)!;
        io.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await io.WriteAllTextAsync(tmp, changelog, ct);
        io.MoveFile(tmp, path, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                io.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
            catch (IOException ex)
            {
                Log.ChangelogChmodFailed(log, ex, path);
            }
        }
    }
}
