namespace FdroidMgr.Api.Models;

/// <summary>
/// An app signing key as the API exposes it — never the store password.
/// </summary>
public record AppKeyResponse(
    int Id,
    string PackageName,
    string Alias,
    string? Branch,
    string FingerprintSha256,
    string StorePath,
    DateTime CreatedUtc);
