namespace FdroidMgr.Api.Abstractions;

/// <summary>
/// The outcome of checking an APK's signature, before anyone decides what to do
/// about it.
/// </summary>
public enum ApkSignatureStatus
{
    /// <summary>
    /// Verification passed and the signing certificate was read.
    /// </summary>
    Verified,

    /// <summary>
    /// The APK carries no signature at all, so one can be added.
    /// </summary>
    Unsigned,

    /// <summary>
    /// Wrong signature or corrupt archive.
    /// </summary>
    Broken,
}

/// <summary>
/// Outcome of checking an APK's signature. <see cref="Fingerprint"/> is only
/// meaningful for <see cref="ApkSignatureStatus.Verified"/>;
/// <see cref="Detail"/> explains the other two.
/// </summary>
public sealed record ApkSignature(ApkSignatureStatus Status, string? Fingerprint = null, string? Detail = null);
