namespace FdroidMgr.Api.Models;

/// <summary>
/// Result of writing app metadata: where it landed and whether the index was
/// rebuilt afterwards.
/// </summary>
public record AppMetadataWrittenResponse(
    string PackageName,
    string Path,
    string Name,
    string License,
    string[] SigningKeyFingerprints,
    bool IndexRegenerated);
