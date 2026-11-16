using FdroidMgr.Api.Abstractions;
using static System.IO.Path;

namespace FdroidMgr.Api.Services;

public sealed class RepoVisibility : IRepoVisibility
{
    public string RelativeTo(string root, string full)
    {
        var prefix = GetFullPath(root).TrimEnd(DirectorySeparatorChar) + DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal) ? full[prefix.Length..] : full;
    }

    public bool IsWithin(string candidate, string root)
    {
        var full = GetFullPath(candidate);
        var rootPrefix = GetFullPath(root).TrimEnd(DirectorySeparatorChar) + DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var segment in full[rootPrefix.Length..]
                     .Split(DirectorySeparatorChar, AltDirectorySeparatorChar))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    public bool IsDeniedPath(string relativePath)
    {
        foreach (var segment in relativePath.Split(DirectorySeparatorChar, AltDirectorySeparatorChar))
        {
            if (IsDeniedSegment(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeniedSegment(string segment)
    {
        if (segment.StartsWith('.'))
        {
            return true;
        }

        return segment.Equals("config.yml", StringComparison.OrdinalIgnoreCase)
               || segment.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
               || segment.EndsWith(".jks", StringComparison.OrdinalIgnoreCase)
               || segment.EndsWith(".keystore", StringComparison.OrdinalIgnoreCase)
               || segment.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
               || segment.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
               || segment.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase);
    }
}
