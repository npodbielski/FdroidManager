namespace FdroidMgr.Api.Models;

/// <summary>
/// Where an uploaded icon ended up, relative to the repo.
/// </summary>
public record AppIconResponse(string PackageName, string Icon);
