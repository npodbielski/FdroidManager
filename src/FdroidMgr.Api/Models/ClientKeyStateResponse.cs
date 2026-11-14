namespace FdroidMgr.Api.Models;

/// <summary>
/// State of the shared F-Droid client key with the secret masked.
/// </summary>
public record ClientKeyStateResponse(
    int Id,
    string Username,
    string KeyPrefix,
    string RepoUrl,
    string RepoBasicAuthUrl,
    DateTime CreatedUtc,
    DateTime? LastUsedUtc,
    DateTime? LastRevealedUtc,
    DateTime? RegeneratedUtc);
