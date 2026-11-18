using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

public interface IApkInspector
{
    Task<ApkMetadata> ReadAsync(string apkPath, CancellationToken ct = default);

    /// <summary>
    /// Checks the APK's signature.
    /// </summary>
    ApkSignature VerifySignature(string apkPath);
}
