using System.Text;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class FdroidMetadataService(FdroidDb db, ILogger<FdroidMetadataService> log, IOService io, IEnvironmentService env)
    : IFdroidMetadataService
{

    public string ResolveRepoRoot(RepoConfig cfg) => RepoIndexService.ResolveRepoRoot(cfg, env);

    public string? GetPath(string packageName, string? repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        return Path.Combine(repoRoot, "metadata", $"{packageName}.yml");
    }

    public static string Render(AppMetadataFields app, IEnumerable<string>? signingKeyFingerprints)
    {
        var sb = new StringBuilder();
        sb.Append("Name: ").Append(EscapeYamlScalar(app.Name)).Append('\n');
        sb.Append("Summary: ").Append(EscapeYamlScalar(app.Summary)).Append('\n');
        sb.Append("Description: ").Append(RenderDescription(app.Description)).Append('\n');
        sb.Append("License: ").Append(EscapeYamlScalar(app.License)).Append('\n');

        AppendOptional(sb, "WebSite", app.Website);
        AppendOptional(sb, "SourceCode", app.SourceCode);
        AppendOptional(sb, "IssueTracker", app.IssueTracker);

        var fingerprints = signingKeyFingerprints?
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (fingerprints is { Length: > 0 })
        {
            sb.Append("AllowedAPKSigningKeys:\n");
            foreach (var fp in fingerprints)
            {
                sb.Append("  - ").Append(fp).Append('\n');
            }
        }

        return sb.ToString();
    }

    public virtual async Task<bool> ExistsAsync(string packageName, CancellationToken ct = default)
    {
        var cfg = await db.RepoConfig.AsNoTracking().FirstAsync(ct);
        var path = GetPath(packageName, ResolveRepoRoot(cfg));
        return path is not null && io.FileExists(path);
    }

    public virtual async Task<string> WriteAsync(string packageName, AppMetadataFields fields, CancellationToken ct = default)
    {
        var cfg = await db.RepoConfig.AsNoTracking().FirstAsync(ct);
        var repoRoot = ResolveRepoRoot(cfg);
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("fdroid repo root is not configured");
        }

        var fingerprints = await db.Keys.AsNoTracking()
            .Where(k => k.PackageName == packageName)
            .Select(k => k.FingerprintSha256)
            .ToListAsync(ct);

        var path = GetPath(packageName, repoRoot)
            ?? throw new InvalidOperationException($"invalid package name: {packageName}");

        var dir = Path.GetDirectoryName(path)!;
        io.CreateDirectory(dir);

        var yaml = Render(fields, fingerprints);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await io.WriteAllTextAsync(tmp, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        io.MoveFile(tmp, path, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                io.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
            catch (IOException) { }
        }

        Log.MetadataWritten(log, path, packageName);
        return path;
    }

    public virtual async Task<string?> ReadAsync(string packageName, CancellationToken ct = default)
    {
        var cfg = await db.RepoConfig.AsNoTracking().FirstAsync(ct);
        var repoRoot = ResolveRepoRoot(cfg);
        var path = GetPath(packageName, repoRoot);
        if (path is null || !io.FileExists(path))
        {
            return null;
        }

        return await io.ReadAllTextAsync(path, ct);
    }

    public static AppMetadataFields ParseAppMetadata(string? yaml)
    {
        var name = "";
        var summary = "";
        var description = "";
        var license = "Unknown";
        string? web = null, source = null, tracker = null;

        var lines = (yaml ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.TrimEnd() == "Description: |")
            {
                var sb = new StringBuilder();
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var l = lines[j];
                    if (l.StartsWith("  ", StringComparison.Ordinal))
                    {
                        sb.AppendLine(l[2..]);
                    }
                    else
                    {
                        break;
                    }
                }
                description = sb.ToString().TrimEnd('\n');
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = UnescapeYamlScalar(line[(idx + 1)..].Trim());
            switch (key)
            {
                case "Name": name = value; break;
                case "Summary": summary = value; break;
                case "Description": description = value; break;
                case "License": license = value; break;
                case "WebSite": web = value; break;
                case "SourceCode": source = value; break;
                case "IssueTracker": tracker = value; break;
            }
        }

        return new AppMetadataFields
        {
            Name = name,
            Summary = summary,
            Description = description,
            License = string.IsNullOrWhiteSpace(license) ? "Unknown" : license,
            Website = string.IsNullOrEmpty(web) ? null : web,
            SourceCode = string.IsNullOrEmpty(source) ? null : source,
            IssueTracker = string.IsNullOrEmpty(tracker) ? null : tracker,
        };
    }

    private static string UnescapeYamlScalar(string value)
    {
        if (value.Length < 2 || !value.StartsWith('"') || !value.EndsWith('"'))
        {
            return value;
        }

        var inner = value[1..^1];
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                i++;
                sb.Append(inner[i] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    _ => inner[i],
                });
            }
            else
            {
                sb.Append(inner[i]);
            }
        }
        return sb.ToString();
    }

    public static string EscapeYamlScalar(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var quoted =
            value.Contains(": ", StringComparison.Ordinal) ||
            value.EndsWith(':') ||
            value.Contains('#') ||
            value.TrimStart() != value ||
            value.TrimEnd() != value ||
            value.Contains('\n') ||
            value.Contains('\r') ||
            StartsWithIndicator(value) ||
            value.Trim() is "null" or "~" or "true" or "false" or "yes" or "no" or "on" or "off";

        if (!quoted)
        {
            return value;
        }

        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
    }

    private static bool StartsWithIndicator(string value)
    {
        var c = value[0];
        return c is '-' or '?' or ':' or '!' or '&' or '*' or '#' or '|' or '>' or '@' or '`' or '"' or '\''
            or '{' or '}' or '[' or ']' or ',' or '%';
    }

    private static void AppendOptional(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        sb.Append(key).Append(": ").Append(EscapeYamlScalar(value)).Append('\n');
    }

    private static string RenderDescription(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return EscapeYamlScalar("");
        }

        var normalized = description.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var multiLine = normalized.Contains('\n');
        var edgeWhitespace = normalized != normalized.Trim();

        if (!multiLine && !edgeWhitespace)
        {
            return EscapeYamlScalar(description);
        }

        var sb = new StringBuilder("|\n");
        foreach (var line in normalized.Split('\n'))
        {
            sb.Append("  ").Append(line).Append('\n');
        }

        return sb.ToString();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 21, Level = LogLevel.Information,
            Message = "Wrote fdroid metadata {Path} for {Pkg}")]
        public static partial void MetadataWritten(ILogger logger, string path, string pkg);
    }
}
