namespace FdroidMgr.Api.Models;

/// <summary>
/// Body of a created API key; the plaintext is returned once.
/// </summary>
public record ApiKeyCreatedResponse(
    int Id,
    string Key,
    string RepoUrl,
    string RepoBasicAuthUrl);
