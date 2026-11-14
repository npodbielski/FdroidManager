using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

public interface IApkInspector
{
    Task<ApkMetadata> ReadAsync(string apkPath, CancellationToken ct = default);

    string SignerFingerprint(string apkPath);
}
