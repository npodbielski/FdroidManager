using System.Text.RegularExpressions;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class RepoInitService : IRepoInitService
{
    private readonly FdroidDb _db;
    private readonly IRepoCommandRunner _runner;
    private readonly ILogger<RepoInitService> _log;

    private readonly IOService _io;

    private readonly IEnvironmentService _env;

    private readonly IPasswordProtector _protector;

    private const string ConfigFileName = "config.yml";
    private const string KeystoreFileName = "keystore.p12";

    private const string DefaultRepoKeyAlias = "fdroid-repo";

    public RepoInitService(FdroidDb db, IRepoCommandRunner runner, ILogger<RepoInitService> log, IOService io, IEnvironmentService env, IPasswordProtector protector)
    {
        _db = db;
        _runner = runner;
        _log = log;
        this._io = io;
        this._env = env;
        this._protector = protector;
        AndroidHome = _env.GetEnvironmentVariable("ANDROID_HOME")
                      ?? _env.GetEnvironmentVariable("FDROID_ANDROID_HOME")
                      ?? "/opt/android-sdk";
        RepoRoot = _env.GetEnvironmentVariable("FDROID_REPO_ROOT") ?? "/data/fdroidrepo";
        MasterKey = _env.GetEnvironmentVariable("FDROID_MASTER_KEY") ?? "";
        if (MasterKey.Length < 16)
        {
            Log.MasterKeyMissing(_log);
        }
    }

    public string MasterKey { get; set; }

    public string RepoRoot { get; set; }

    public string AndroidHome { get; set; }

    public bool IsInitialized()
        => _io.DirectoryExists(RepoRoot) &&
           (_io.FileExists(Path.Combine(RepoRoot, ConfigFileName))
            || _io.FileExists(Path.Combine(RepoRoot, KeystoreFileName)));

    internal static string DefaultKeyAlias() => DefaultRepoKeyAlias;

    public string ResolveRepoUrl(string? storedUrl = null)
    {
        var publicBase = (_env.GetEnvironmentVariable("FDROID_PUBLIC_URL") ?? "").TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(publicBase))
        {
            return publicBase + "/fdroid/repo";
        }

        var repoUrl = _env.GetEnvironmentVariable("FDROID_REPO_URL");
        if (!string.IsNullOrWhiteSpace(repoUrl))
        {
            return repoUrl;
        }

        if (!string.IsNullOrWhiteSpace(storedUrl))
        {
            return storedUrl;
        }

        return "http://localhost:5000/fdroid/repo";
    }

    public static void Validate(RepoInitRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RepoName))
        {
            throw new ArgumentException("repoName is required");
        }

        if (req.KeyAlias is { } alias && string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("keyAlias is invalid");
        }
    }

    public async Task<RepoInitResult> InitAsync(RepoInitRequest req, CancellationToken ct = default)
    {

        var repoUrl = ResolveRepoUrl();
        Validate(req);

        var root = RepoRoot;
        if (!_io.DirectoryExists(root))
        {
            _io.CreateDirectory(root);
        }

        var existing = _io.DirectoryExists(root) && (
            _io.FileExists(Path.Combine(root, ConfigFileName))
            || _io.FileExists(Path.Combine(root, KeystoreFileName)));

        string? backupPath = null;
        if (existing)
        {
            if (!req.Force)
            {
                throw new RepoAlreadyInitializedException(
                    "repo already initialized", CreateBackupAsync(ct));
            }

            backupPath = CreateBackupAsync(ct);

            ClearRepoRoot(root);
        }

        var keystore = req.KeystorePath ?? Path.Combine(root, KeystoreFileName);
        var alias = string.IsNullOrWhiteSpace(req.KeyAlias) ? DefaultKeyAlias() : req.KeyAlias!;
        var initArgs = new List<string>
        {
            "init", "--no-prompt",
            "--android-home", AndroidHome,
        };
        if (!string.IsNullOrEmpty(req.KeystorePath))
        {
            var k = Path.GetFullPath(req.KeystorePath);
            initArgs.Add("--keystore");
            initArgs.Add(k);
            keystore = k;
        }

        initArgs.Add("--repo-keyalias");
        initArgs.Add(alias);
        var initRes = await _runner.RunAsync("fdroid", initArgs, root, ct);
        if (!initRes.Success)
        {
            throw new FdroidUpdateException(
                $"fdroid init failed: {Trim(initRes.Stderr)}");
        }

        PatchConfig(root, req, repoUrl);

        var create = await _runner.RunAsync("fdroid", new List<string> { "update", "-c" }, root, ct);
        if (!create.Success)
        {
            throw new FdroidUpdateException($"fdroid update -c failed: {Trim(create.Stderr)}");
        }

        var update = await _runner.RunAsync("fdroid", new List<string> { "update" }, root, ct);
        if (!update.Success)
        {
            throw new FdroidUpdateException($"fdroid update failed: {Trim(update.Stderr)}");
        }

        var keystorePass = ReadConfigValue(root, "keystorepass");
        var encrypted = string.IsNullOrEmpty(keystorePass)
            ? null
            : _protector.Protect(keystorePass);

        var cfg = await _db.RepoConfig.FirstOrDefaultAsync(ct);
        var repoPath = Path.Combine(root, "repo");
        if (cfg is null)
        {
            cfg = new RepoConfig
            {
                Id = 1,
                RepoPath = repoPath,
                KeyPath = Path.Combine(root, "keys"),
                RepoUrl = repoUrl,
                RepoName = req.RepoName,
                RepoDescription = req.RepoDescription,
                RepoKeystorePassEnc = encrypted,
                RepoKeySha256 = ComputeRepoKeySha256(keystore, keystorePass, alias),
                BackupPath = backupPath,
                LastIndexBuiltUtc = DateTime.UtcNow,
                LastIndexError = null,
            };
            _db.RepoConfig.Add(cfg);
        }
        else
        {
            cfg.RepoPath = repoPath;
            cfg.RepoUrl = repoUrl;
            cfg.RepoName = req.RepoName;
            cfg.RepoDescription = req.RepoDescription;
            cfg.RepoKeystorePassEnc = encrypted;
            cfg.RepoKeySha256 = ComputeRepoKeySha256(keystore, keystorePass, alias);
            cfg.BackupPath = backupPath;
            cfg.LastIndexBuiltUtc = DateTime.UtcNow;
            cfg.LastIndexError = null;
        }

        await _db.SaveChangesAsync(ct);

        Log.RepoInitialized(_log,
            root, alias, cfg.RepoKeySha256);

        return new RepoInitResult
        {
            RepoUrl = repoUrl,
            RepoName = req.RepoName,
            RepoPath = repoPath,
            KeyAlias = alias,
            RepoKeySha256 = cfg.RepoKeySha256 ?? "",
            BackupPath = backupPath,
        };
    }

    private string CreateBackupAsync(CancellationToken ct)
    {
        var root = RepoRoot;
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var suffix = "";
        var i = 1;
        string target;
        do
        {
            target = root.TrimEnd(Path.DirectorySeparatorChar) + $"-backup-{stamp}{suffix}";
            suffix = i == 1 ? "" : $"-{i}";
            i++;
            ct.ThrowIfCancellationRequested();
        } while (_io.DirectoryExists(target));

        Log.RepoBackup(_log, root, target);
        DirectoryCopy(root, target, ct);
        return target;
    }

    private void DirectoryCopy(string source, string dest, CancellationToken ct)
    {
        _io.CreateDirectory(dest);
        foreach (var file in _io.EnumerateFilesRecursive(source))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            _io.CopyFile(file, targetFile, overwrite: true);
        }
    }

    private void ClearRepoRoot(string root)
    {
        if (!_io.DirectoryExists(root))
        {
            return;
        }

        foreach (var entry in _io.EnumerateFileSystemEntries(root))
        {
            try
            {
                if (_io.DirectoryExists(entry))
                {
                    _io.DeleteDirectory(entry, recursive: true);
                }
                else
                {
                    _io.DeleteFile(entry);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"backup failed — could not clear {entry}: {ex.Message}", ex);
            }
        }
    }

    private void PatchConfig(string root, RepoInitRequest req, string repoUrl)
    {
        var path = Path.Combine(root, ConfigFileName);
        if (!_io.FileExists(path))
        {
            throw new FdroidUpdateException("fdroid init did not create config.yml");
        }

        var lines = _io.ReadAllLines(path).ToList();
        SetKey(lines, "repo_url", repoUrl);
        SetKey(lines, "repo_name", req.RepoName);
        if (!string.IsNullOrEmpty(req.RepoDescription))
        {
            SetKey(lines, "repo_description", req.RepoDescription);
        }

        _io.WriteAllLines(path, lines);
    }

    private static void SetKey(List<string> lines, string key, string value)
    {
        var prefix = key + ":";
        for (var i = 0; i < lines.Count; i++)
        {

            if (lines[i].StartsWith(prefix, StringComparison.Ordinal) && !lines[i].StartsWith("#"))
            {
                lines[i] = $"{prefix} {value}";
                return;
            }
        }

        lines.Add($"{prefix} {value}");
    }

    private string? ReadConfigValue(string root, string key)
    {
        var path = Path.Combine(root, ConfigFileName);
        if (!_io.FileExists(path))
        {
            return null;
        }

        var prefix = key + ":";
        foreach (var line in _io.ReadLines(path))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal) || line.StartsWith('#'))
            {
                continue;
            }

            return line[prefix.Length..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private string ComputeRepoKeySha256(string keystore, string? password, string alias)
    {
        if (string.IsNullOrEmpty(password) || !_io.FileExists(keystore))
        {
            return "";
        }

        try
        {
            var fp = KeyService.GetFingerprint(keystore, password, alias);

            return fp == "unknown" ? "" : fp;
        }
        catch (Exception ex)
        {
            Log.RepoKeyFingerprintUnreadable(_log, ex.Message);
            return "";
        }
    }

    private static string Trim(string? s) => WhiteSpaceRegex().Replace(s ?? "", " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpaceRegex();

    private static partial class Log
    {
        [LoggerMessage(EventId = 25, Level = LogLevel.Warning,
            Message = "FDROID_MASTER_KEY is missing/short — repo keystore password will NOT be stored encrypted")]
        public static partial void MasterKeyMissing(ILogger logger);

        [LoggerMessage(EventId = 26, Level = LogLevel.Information,
            Message = "Repo initialized at {Root} (alias {Alias}, sha256 {Sha})")]
        public static partial void RepoInitialized(ILogger logger, string root, string alias, string sha);

        [LoggerMessage(EventId = 27, Level = LogLevel.Information,
            Message = "Backing up existing repo {Root} → {Target}")]
        public static partial void RepoBackup(ILogger logger, string root, string target);

        [LoggerMessage(EventId = 28, Level = LogLevel.Warning,
            Message = "Could not read repo key fingerprint: {Error}")]
        public static partial void RepoKeyFingerprintUnreadable(ILogger logger, string error);
    }
}
