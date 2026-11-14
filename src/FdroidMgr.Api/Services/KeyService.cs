using System.Diagnostics;
using System.Security.Cryptography;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public class KeyService(FdroidDb db, IPasswordProtector protector, IOService io, IEnvironmentService env)
    : IKeyService
{
    private const string KeyPassEnvVar = "FDROID_KEYPASS";

    public async Task<AppKey> GenerateKeyAsync(string packageName, string? branch = null, CancellationToken ct = default)
    {
        var cfg = await GetRepoConfigAsync(ct);
        var root = cfg.KeyPath;
        if (!string.IsNullOrWhiteSpace(root))
        {
            io.CreateDirectory(root);
        }

        var alias = "release";
        var dir = Sanitize(packageName);
        if (!string.IsNullOrEmpty(branch))
        {
            var branchDir = Sanitize(branch);
            dir = Path.Combine(dir, branchDir);
            alias = Sanitize(branch);
        }

        io.CreateDirectory(Path.Combine(root, dir));

        var storePath = Path.Combine(dir, "release.keystore");
        var fullPath = Path.Combine(root, storePath);
        if (io.FileExists(fullPath))
        {
            io.DeleteFile(fullPath);
        }

        var password = GeneratePassword(32);
        RunTool("keytool", PassEnv(password),
            "-genkeypair", "-v",
            "-storetype", "PKCS12",
            "-keystore", fullPath,
            "-storepass:env", KeyPassEnvVar,
            "-keypass:env", KeyPassEnvVar,
            "-alias", alias,
            "-keysize", "4096",
            "-keyalg", "RSA",
            "-validity", "10000",
            "-dname", DNameFor(packageName, branch));

        var fingerprint = GetFingerprint(fullPath, password, alias);
        var encrypted = protector.Protect(password);

        var key = new AppKey
        {
            PackageName = packageName,
            Alias = alias,
            Branch = branch,
            StorePath = storePath,
            FingerprintSha256 = fingerprint,
            PasswordEncrypted = encrypted,
            CreatedUtc = DateTime.UtcNow,
        };

        db.Keys.Add(key);
        await db.SaveChangesAsync(ct);
        return key;
    }

    public async Task<AppKey> ImportKeyAsync(string packageName, Stream keystoreStream,
        string storePassword, string keyPassword, string alias, string? branch = null, CancellationToken ct = default)
    {
        var cfg = await GetRepoConfigAsync(ct);
        var root = cfg.KeyPath;
        if (!string.IsNullOrWhiteSpace(root))
        {
            io.CreateDirectory(root);
        }

        var dir = Sanitize(packageName);
        if (!string.IsNullOrEmpty(branch))
        {
            dir = Path.Combine(dir, Sanitize(branch));
        }

        io.CreateDirectory(Path.Combine(root, dir));

        var storePath = Path.Combine(dir, "release.keystore");
        var fullPath = Path.Combine(root, storePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "fdroidmgr-keys");
        io.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".keystore");
        try
        {
            await using (var fs = io.CreateFile(tempPath))
            {
                await keystoreStream.CopyToAsync(fs, ct);
            }

            var fingerprint = GetFingerprint(tempPath, storePassword, alias);
            if (fingerprint == "unknown")
            {
                throw new InvalidOperationException("unreadable keystore (wrong store password or alias)");
            }

            io.CopyFile(tempPath, fullPath, overwrite: true);

            var encrypted = protector.Protect(keyPassword);

            var key = new AppKey
            {
                PackageName = packageName,
                Alias = alias,
                Branch = branch,
                StorePath = storePath,
                FingerprintSha256 = fingerprint,
                PasswordEncrypted = encrypted,
                CreatedUtc = DateTime.UtcNow,
            };

            db.Keys.Add(key);
            await db.SaveChangesAsync(ct);
            return key;
        }
        finally
        {
            try { io.DeleteFile(tempPath); } catch (IOException) { }
        }
    }

    public virtual async Task<(AppKey Key, string Password)> GetAvailableKeyAsync(string packageName, string? branch = null, CancellationToken ct = default)
    {
        AppKey? key;
        if (branch is null)
        {
            key = await db.Keys.Where(k => k.PackageName == packageName && k.Branch == null).OrderByDescending(k => k.Id).FirstOrDefaultAsync(ct);
        }
        else
        {
            var exact = await db.Keys.Where(k => k.PackageName == packageName && k.Branch == branch).OrderByDescending(k => k.Id).FirstOrDefaultAsync(ct);
            key = exact ?? await db.Keys.Where(k => k.PackageName == packageName && k.Branch == null).OrderByDescending(k => k.Id).FirstOrDefaultAsync(ct);
        }

        if (key is null)
        {
            throw new InvalidOperationException($"No signing key for {packageName}" + (branch is null ? "" : $" branch '{branch}'"));
        }

        var password = protector.Unprotect(key.PasswordEncrypted);
        if (protector.IsLegacy(key.PasswordEncrypted))
        {
            key.PasswordEncrypted = protector.Protect(password);
            await db.SaveChangesAsync(ct);
        }

        return (key, password);
    }

    public virtual async Task SignApkAsync(string packageName, string unsignedPath, string signedPath, string? branch = null, CancellationToken ct = default)
    {
        var (key, password) = await GetAvailableKeyAsync(packageName, branch, ct);
        var cfg = await GetRepoConfigAsync(ct);
        var fullPath = Path.Combine(cfg.KeyPath, key.StorePath);

        RunTool(ApkService.ResolveApkSignerPath(env),
            new Dictionary<string, string> { ["FDROID_KS_PASS"] = password, ["FDROID_KEY_PASS"] = password },
            "sign",
            "--ks", fullPath,
            "--ks-pass", "env:FDROID_KS_PASS",
            "--ks-key-alias", key.Alias,
            "--key-pass", "env:FDROID_KEY_PASS",
            "--v1-signing-enabled", "true",
            "--v2-signing-enabled", "true",
            "--out", signedPath,
            unsignedPath);
    }

    private async Task<RepoConfig> GetRepoConfigAsync(CancellationToken ct)
        => await db.RepoConfig.FirstAsync(ct);

    private static string GeneratePassword(int len)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(len)).TrimEnd('=').Replace('/', 'x');

    private static string Sanitize(string s)
        => string.Join("_", s.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    private static string DNameFor(string package, string? branch)
        => $"CN={package}, OU=F-Droid{(branch is null ? "" : $" ({branch})")}, O=F-Droid Repo";

    private static Dictionary<string, string> PassEnv(string password)
        => new() { [KeyPassEnvVar] = password };

    private static void RunTool(string tool, IReadOnlyDictionary<string, string>? passEnv, params string[] args)
    {
        var psi = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (passEnv is not null)
        {
            foreach (var (name, value) in passEnv)
            {
                psi.Environment[name] = value;
            }
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {tool}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !(tool == "apksigner" && stderr.Contains("WARNING")))
        {
            throw new InvalidOperationException($"{tool} failed: {stdout}\n{stderr}");
        }
    }

    public static string GetFingerprint(Stream keystoreStream, string password, string alias, IOService io)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fdroidmgr-keys");
        io.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".keystore");
        try
        {
            using (var fs = io.CreateFile(tempPath))
            {
                keystoreStream.CopyTo(fs);
            }

            return GetFingerprint(tempPath, password, alias);
        }
        finally
        {
            try { io.DeleteFile(tempPath); } catch (IOException) { }
        }
    }

    public static string GetFingerprint(string keystorePath, string password, string alias)
    {
        var psi = new ProcessStartInfo("keytool")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { [KeyPassEnvVar] = password },
        };
        psi.ArgumentList.Add("-list");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("-keystore");
        psi.ArgumentList.Add(keystorePath);
        psi.ArgumentList.Add("-storepass:env");
        psi.ArgumentList.Add(KeyPassEnvVar);
        psi.ArgumentList.Add("-alias");
        psi.ArgumentList.Add(alias);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start keytool");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        var match = System.Text.RegularExpressions.Regex.Match(
            output.Replace("\n", " "), "(?:[0-9A-Fa-f]{2}[: ]){31}[0-9A-Fa-f]{2}");
        return match.Success
            ? match.Value.Replace(":", "").Replace(" ", "").ToLowerInvariant()
            : "unknown";
    }
}
