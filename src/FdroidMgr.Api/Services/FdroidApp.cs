namespace FdroidMgr.Api.Services;

public record FdroidApp(
    string PackageName,
    string Name,
    string Summary,
    string Description,
    string License,
    string? Web,
    string? Source,
    string? Tracker,
    string? Icon,
    IReadOnlyDictionary<string, string[]>? Screenshots,
    IReadOnlyList<FdroidVersion> Versions);
