using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public record ApkMetadata
{
    public string PackageName { get; init; } = "";

    public int VersionCode { get; init; }

    public string VersionName { get; init; } = "";

    public int MinSdk { get; init; }

    public int TargetSdk { get; init; }

    public string Label { get; init; } = "";

    public string[] Permissions { get; init; } = [];

    public string? LaunchableActivity { get; init; }

    public string Sha256 { get; init; } = "";
}

public partial class ApkService(ILogger<ApkService> log, IOService io, IEnvironmentService env) : IApkInspector
{
    public ApkSignature VerifySignature(string apkPath)
    {
        var exe = ApkSignerPath;
        if (!io.FileExists(exe))
        {
            throw new InvalidOperationException("apksigner not found — cannot verify signature");
        }

        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("verify");
        psi.ArgumentList.Add("--print-certs");
        psi.ArgumentList.Add(apkPath);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start apksigner");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        var digest = System.Text.RegularExpressions.Regex
            .Match(stdout, @"SHA-256 digest:\s*([0-9a-fA-F]+)");

        if (p.ExitCode == 0 && digest.Success)
        {
            return new ApkSignature(ApkSignatureStatus.Verified, digest.Groups[1].Value.ToLowerInvariant());
        }

        var complaint = FirstLine(stderr.Length > 0 ? stderr : stdout);

        try
        {
            return HasV1Signature(apkPath) || HasSigningBlock(apkPath)
                ? Broken(complaint)
                : new ApkSignature(ApkSignatureStatus.Unsigned);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // Wrong signature or corrupt archive.
            return Broken(complaint);
        }
    }

    private static ApkSignature Broken(string complaint)
        => new(ApkSignatureStatus.Broken, Detail: complaint.Length > 0 ? complaint : "signature does not verify");

    // v1 (JAR) signing leaves a signature block next to the manifest.
    private bool HasV1Signature(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        return zip.Entries.Any(e =>
            e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(e.FullName).ToUpperInvariant() is ".SF" or ".RSA" or ".DSA" or ".EC");
    }

    // v2/v3 signing leaves no file behind, only an "APK Sig Block 42" marker just before the central directory.
    private bool HasSigningBlock(string apkPath)
    {
        using var fs = io.OpenRead(apkPath);
        var tail = fs.Length >= 38 ? ReadAt(fs, fs.Length - 22, 22) : null;
        if (tail is null || tail[0] != 'P' || tail[1] != 'K' || tail[2] != 5 || tail[3] != 6)
        {
            return false;
        }

        var centralDirectory = (long)BitConverter.ToUInt32(tail, 16);
        var magic = centralDirectory >= 16 ? ReadAt(fs, centralDirectory - 16, 16) : null;
        return magic is not null && Encoding.ASCII.GetString(magic) == "APK Sig Block 42";
    }

    private static byte[]? ReadAt(Stream fs, long offset, int count)
    {
        if (offset < 0 || offset + count > fs.Length)
        {
            return null;
        }

        fs.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        return fs.ReadAtLeast(buffer, count, throwOnEndOfStream: false) == count ? buffer : null;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', '\r').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return line.Length > 300 ? line[..300] : line;
    }

    private string AaptPath
        => env.GetEnvironmentVariable("AAPT")
           ?? Path.Combine(env.GetEnvironmentVariable("ANDROID_HOME") ?? "", "build-tools", "35.0.0", "aapt");

    private string ApkSignerPath => ResolveApkSignerPath(env);

    public static string ResolveApkSignerPath(IEnvironmentService env)
        => env.GetEnvironmentVariable("APKSIGNER")
           ?? Path.Combine(env.GetEnvironmentVariable("ANDROID_HOME") ?? "", "build-tools", "35.0.0", "apksigner");

    public async Task<ApkMetadata> ReadAsync(string apkPath, CancellationToken ct = default)
    {
        if (!io.FileExists(apkPath))
        {
            throw new FileNotFoundException("APK not found", apkPath);
        }

        var sha = await ComputeSha256Async(apkPath, io, ct);

        if (!io.FileExists(AaptPath))
        {
            return await ReadManagedAsync(apkPath, sha, ct);
        }

        try
        {
            return await ReadBadgingAsync(apkPath, sha, ct);
        }
        catch (Exception ex)
        {
            Log.AaptFailed(log, ex);
        }

        return await ReadManagedAsync(apkPath, sha, ct);
    }

    private async Task<ApkMetadata> ReadBadgingAsync(string apkPath, string sha, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(AaptPath) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("dump");
        psi.ArgumentList.Add("badging");
        psi.ArgumentList.Add(apkPath);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start aapt");
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return p.ExitCode != 0 ? throw new InvalidOperationException($"aapt failed: {err}") : ParseBadging(output, sha);
    }

    internal static ApkMetadata ParseBadging(string output, string sha)
    {
        static string GrabQuoted(string text, string prefix)
        {
            var idx = text.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0)
            {
                return "";
            }

            var open = text.IndexOf('\'', idx);
            if (open < 0)
            {
                return "";
            }

            var close = text.IndexOf('\'', open + 1);
            return close < 0 ? "" : text[(open + 1)..close];
        }

        var packageName = GrabQuoted(output, "package: name=");
        var versionCode = GrabQuoted(output, "versionCode=") is { } vc && int.TryParse(vc, out var vcv) ? vcv : 0;
        var versionName = GrabQuoted(output, "versionName=");
        var minSdk = TryInt(Grab("sdkVersion:")) ?? 0;
        var targetSdk = TryInt(Grab("targetSdkVersion:")) ?? 0;
        var label = Grab("application-label:");
        var launchable = Grab("launchable-activity:");

        var perms = output.Split('\n')
            .Where(l => l.StartsWith("uses-permission: name="))
            .Select(l => l.Split('\'')[1])
            .ToArray();

        return new ApkMetadata
        {
            PackageName = packageName,
            VersionCode = versionCode,
            VersionName = versionName,
            MinSdk = minSdk,
            TargetSdk = targetSdk,
            Label = label,
            Permissions = perms,
            LaunchableActivity = launchable.Length > 0 ? launchable : null,
            Sha256 = sha,
        };

        string Grab(string prefix)
        {
            var line = output.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            if (line is null)
            {
                return "";
            }

            var idx = line.IndexOf('\'');
            if (idx < 0)
            {
                return "";
            }

            var end = line.IndexOf('\'', idx + 1);
            return end < 0 ? line[(idx + 1)..] : line[(idx + 1)..end];
        }
    }

    private async Task<ApkMetadata> ReadManagedAsync(string apkPath, string sha, CancellationToken ct)
    {

        await Task.CompletedTask;
        var hasDex = false;
        await using var zip = await ZipFile.OpenReadAsync(apkPath, ct);
        foreach (var e in zip.Entries)
        {
            if (e.FullName == "classes.dex")
            {
                hasDex = true;
            }
        }
        return new ApkMetadata { Sha256 = sha, VersionName = hasDex ? "?" : "" };
    }

    private static int? TryInt(string s) => int.TryParse(s.Trim(), out var v) ? v : null;

    public static async Task<string> ComputeSha256Async(string path, IOService io, CancellationToken ct)
    {
        await using var fs = io.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ToManifestJson(ApkMetadata m)
        => JsonSerializer.Serialize(new
        {
            m.PackageName,
            m.VersionCode,
            m.VersionName,
            m.MinSdk,
            m.TargetSdk,
            m.Permissions,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static partial class Log
    {
        [LoggerMessage(EventId = 19, Level = LogLevel.Warning,
            Message = "aapt failed, falling back to managed reader")]
        public static partial void AaptFailed(ILogger logger, Exception ex);
    }
}
