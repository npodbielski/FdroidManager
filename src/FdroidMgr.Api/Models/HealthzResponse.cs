namespace FdroidMgr.Api.Models;

/// <summary>
/// Liveness probe body: the boot rebuild may still be running while the API
/// already serves requests.
/// </summary>
public record HealthzResponse(string Status, bool BootCompleted);
