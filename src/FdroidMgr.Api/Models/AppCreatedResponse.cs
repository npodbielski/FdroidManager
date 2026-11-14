namespace FdroidMgr.Api.Models;

/// <summary>
/// Body of a created app: where its metadata landed and which signing key
/// protects it.
/// </summary>
public record AppCreatedResponse(
    string PackageName,
    string Path,
    string Name,
    string License,
    string[] SigningKeyFingerprints);
