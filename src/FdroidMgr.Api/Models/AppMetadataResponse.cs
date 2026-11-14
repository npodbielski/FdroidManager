namespace FdroidMgr.Api.Models;

/// <summary>
/// App metadata as stored on disk, plus the file it came from.
/// </summary>
public record AppMetadataResponse(string Path, string Yaml);
