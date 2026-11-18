using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public class ReleaseConflictException(string message) : Exception(message);

public partial class ReleaseService(
    FdroidDb db,
    IApkInspector apk,
    IKeyService keys,
    IRepoIndexService index,
    IFdroidMetadataService metadata,
    IFdroidRepoService repo,
    ILogger<ReleaseService> log,
    IOService io,
    IRepoLock repoLock)
    : IReleaseService
{

    public async Task<ReleaseDto> PublishAsync(string packageName, IFormFile apk1, string? changelog, string? branch = null, CancellationToken ct = default)
    {
        var lease = await repoLock.AcquireAsync(ct);
        try
        {
            return await PublishLockedAsync(packageName, apk1, changelog, branch, lease, ct);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task<ReleaseDto> PublishLockedAsync(string packageName, IFormFile apk1, string? changelog, string? branch, IRepoLockLease lease, CancellationToken ct)
    {
        var cfg = await db.RepoConfig.AsNoTracking().FirstAsync(ct);

        if (!await metadata.ExistsAsync(packageName, ct))
        {
            throw new KeyNotFoundException($"App {packageName} not found");
        }

        var index1 = await repo.ReadIndexAsync(cfg, ct);

        var tempDir = Path.Combine(Path.GetTempPath(), "fdroidmgr-uploads");
        io.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".apk");
        await using (var fs = io.CreateFile(tempPath))
        {
            await apk1.CopyToAsync(fs, ct);
        }

        try
        {
            ApkMetadata meta;
            try
            {
                meta = await apk.ReadAsync(tempPath, ct);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or InvalidOperationException)
            {
                throw new InvalidOperationException($"APK could not be parsed: {ex.Message}", ex);
            }
            if (meta.PackageName != packageName)
            {
                throw new InvalidOperationException($"APK package '{meta.PackageName}' does not match app '{packageName}'");
            }

            var key = await keys.GetAvailableKeyAsync(packageName, branch, ct);
            var signature = apk.VerifySignature(tempPath);

            if (signature.Status == ApkSignatureStatus.Broken)
            {
                throw new InvalidOperationException(
                    $"APK signature does not verify, and it was not re-signed: {signature.Detail}");
            }

            if (signature.Status == ApkSignatureStatus.Unsigned)
            {
                Log.SigningUnsignedApk(log, packageName);
                var signedPath = tempPath + ".signed.apk";
                await keys.SignApkAsync(packageName, tempPath, signedPath, branch, ct);

                io.DeleteFile(tempPath);
                io.MoveFile(signedPath, tempPath);

                meta = await apk.ReadAsync(tempPath, ct);
            }
            else if (!string.Equals(signature.Fingerprint, key.Key.FingerprintSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"APK signed with {signature.Fingerprint}, expected app key {key.Key.FingerprintSha256}");
            }

            var lastCode = FdroidRepoService.LastVersionCode(index1, packageName);
            var vc = meta.VersionCode > 0 ? meta.VersionCode : (lastCode + 1);

            var alreadyHas = index1?.Apps.FirstOrDefault(a => a.PackageName == packageName)?
                .Versions.Any(v => v.VersionCode == vc) == true;
            if (alreadyHas)
            {
                throw new ReleaseConflictException($"versionCode {vc} already published");
            }

            if (vc <= lastCode)
            {
                throw new InvalidOperationException($"versionCode {vc} is not greater than the last release ({lastCode})");
            }

            io.CreateDirectory(cfg.RepoPath);
            var dest = Path.Combine(cfg.RepoPath, SafeUploadName(apk1.FileName));
            io.CopyFile(tempPath, dest, overwrite: true);

            var changelogText = string.IsNullOrWhiteSpace(changelog) ? null : changelog.Trim();
            if (changelogText is not null)
            {
                await repo.WriteChangelogAsync(cfg, packageName, vc, changelogText, ct);
            }

            await index.GenerateStrictAsync(cfg, lease: lease, ct: ct);

            Log.ReleasePublished(log, packageName, vc);

            var fresh = await repo.ReadIndexAsync(cfg, ct);
            var version = fresh?.Apps.FirstOrDefault(a => a.PackageName == packageName)?
                .Versions.FirstOrDefault(v => v.VersionCode == vc);
            if (version is not null)
            {
                return ToDto(packageName, version, () => changelogText);
            }

            return new ReleaseDto
            {
                PackageName = packageName,
                VersionCode = vc,
                VersionName = meta.VersionName,
                ApkFileName = $"{packageName}_{vc}.apk",
                Sha256 = meta.Sha256,
                SizeBytes = new FileInfo(dest).Length,
                SignerSha256 = key.Key.FingerprintSha256,
                MinSdk = meta.MinSdk > 0 ? meta.MinSdk : null,
                TargetSdk = meta.TargetSdk > 0 ? meta.TargetSdk : null,
                Permissions = meta.Permissions,
                ChangelogLoader = () => changelogText,
            };
        }
        finally
        {
            try { io.DeleteFile(tempPath); } catch (IOException) { }
        }
    }

    private static ReleaseDto ToDto(string packageName, FdroidVersion v, Func<string?> changelogLoader)
        => new()
        {
            PackageName = packageName,
            VersionCode = v.VersionCode,
            VersionName = v.VersionName,
            ApkFileName = v.ApkName,
            Sha256 = v.Sha256,
            SizeBytes = v.SizeBytes,
            SignerSha256 = v.SignerSha256,
            MinSdk = v.MinSdk,
            TargetSdk = v.TargetSdk,
            Permissions = v.Permissions,
            ChangelogLoader = changelogLoader,
        };

    private static string SafeUploadName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "upload.apk";
        }

        var safe = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            safe[i] = char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_';
        }

        name = new string(safe);
        if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            name += ".apk";
        }

        return name;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 17, Level = LogLevel.Information,
            Message = "APK is unsigned, signing with app key for {Pkg}")]
        public static partial void SigningUnsignedApk(ILogger logger, string pkg);

        [LoggerMessage(EventId = 18, Level = LogLevel.Information,
            Message = "Published {Pkg} {Vc} via fdroid update")]
        public static partial void ReleasePublished(ILogger logger, string pkg, int vc);
    }
}
