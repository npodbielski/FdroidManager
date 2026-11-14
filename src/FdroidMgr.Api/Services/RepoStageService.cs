using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Services;

public partial class RepoStageService(
    IOService io,
    IConfiguration config,
    IEnvironmentService env,
    IRepoVisibility visibility,
    ILogger<RepoStageService> log)
    : IRepoStageService
{
    private const string PublicPathSetting = "FDROID_REPO_PUBLIC_PATH";

    public string? ResolvePublicDir()
    {
        var configured = config[PublicPathSetting];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = env.GetEnvironmentVariable(PublicPathSetting);
        }

        return string.IsNullOrWhiteSpace(configured) ? null : Path.GetFullPath(configured);
    }

    public void Stage(RepoConfig cfg)
    {
        var target = ResolvePublicDir();
        if (target is null)
        {
            return;
        }

        var source = string.IsNullOrWhiteSpace(cfg.RepoPath) ? null : Path.GetFullPath(cfg.RepoPath);
        if (source is null || !io.DirectoryExists(source))
        {
            Log.SourceMissing(log, source ?? "(unset)");
            return;
        }

        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            Log.TargetIsSource(log, target);
            return;
        }

        io.CreateDirectory(target);
        var copied = 0;
        var skipped = 0;
        var live = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in io.EnumerateFilesRecursive(source))
        {
            var relative = visibility.RelativeTo(source, file);
            if (visibility.IsDeniedPath(relative))
            {
                skipped++;
                continue;
            }

            var dest = Path.Combine(target, relative);
            live.Add(relative);
            if (!NeedsCopy(source: file, dest: dest, io: io))
            {
                continue;
            }

            io.CreateDirectory(Path.GetDirectoryName(dest)!);
            io.CopyFile(file, dest, overwrite: true);
            copied++;
        }

        var removed = Prune(target, target, live);
        Log.Staged(log, source, target, copied, removed, skipped);
    }

    private static bool NeedsCopy(string source, string dest, IOService io)
    {
        if (!io.FileExists(dest))
        {
            return true;
        }

        if (io.GetLength(dest) != io.GetLength(source))
        {
            return true;
        }

        return io.GetLastWriteTimeUtc(dest) != io.GetLastWriteTimeUtc(source);
    }

    private int Prune(string target, string dir, HashSet<string> live)
    {
        var removed = 0;
        foreach (var entry in io.EnumerateFileSystemEntries(dir))
        {
            var relative = visibility.RelativeTo(target, entry);
            if (visibility.IsDeniedPath(relative))
            {
                continue;
            }

            if (io.DirectoryExists(entry))
            {
                removed += Prune(target, entry, live);
                if (!io.EnumerateFileSystemEntries(entry).Any())
                {
                    io.DeleteDirectory(entry, recursive: false);
                }

                continue;
            }

            if (live.Contains(relative))
            {
                continue;
            }

            io.DeleteFile(entry);
            removed++;
        }

        return removed;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 60, Level = LogLevel.Warning,
            Message = "cannot stage repo files for serving: output directory {Source} is missing")]
        public static partial void SourceMissing(ILogger logger, string source);

        [LoggerMessage(EventId = 61, Level = LogLevel.Warning,
            Message = "repo public path {Target} is the fdroid output directory; staging disabled")]
        public static partial void TargetIsSource(ILogger logger, string target);

        [LoggerMessage(EventId = 62, Level = LogLevel.Information,
            Message = "staged repo files from {Source} to {Target}: {Copied} copied, {Removed} removed, {Skipped} skipped")]
        public static partial void Staged(ILogger logger, string source, string target, int copied, int removed, int skipped);
    }
}
