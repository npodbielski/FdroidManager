namespace FdroidMgr.Api.Services;

public record FdroidVersion(
    int VersionCode,
    string VersionName,
    string ApkName,
    string Sha256,
    long SizeBytes,
    string? SignerSha256,
    int? MinSdk,
    int? TargetSdk,
    string[] Permissions);
