namespace FdroidMgr.Api.Models;

/// <summary>
/// The fields an app update applied.
/// </summary>
public record AppUpdatedResponse(string PackageName, string Name, string License);
