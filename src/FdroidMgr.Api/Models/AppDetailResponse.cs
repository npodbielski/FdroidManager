namespace FdroidMgr.Api.Models;

/// <summary>
/// Everything the app detail page shows: metadata from the index, the signing
/// keys on record, and the published versions.
/// </summary>
public record AppDetailResponse(
    string PackageName,
    string Name,
    string Summary,
    string Description,
    string License,
    string? Web,
    string? Source,
    string? Tracker,
    string? Icon,
    IReadOnlyList<string> Screenshots,
    IReadOnlyList<AppKeyResponse> Keys,
    IReadOnlyList<AppVersionResponse> Versions);
