namespace FdroidMgr.Api.Models;

/// <summary>
/// A version of an app as listed by the app detail endpoint, newest first.
/// </summary>
public record AppVersionResponse(
    int VersionCode,
    string VersionName,
    string ApkName,
    string Sha256,
    long SizeBytes,
    string? SignerSha256,
    int? MinSdk,
    int? TargetSdk,
    string[] Permissions);
