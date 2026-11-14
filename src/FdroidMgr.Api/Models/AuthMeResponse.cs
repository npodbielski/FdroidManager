namespace FdroidMgr.Api.Models;

/// <summary>
/// The signed-in identity as the Web UI renders it.
/// </summary>
public record AuthMeResponse(
    string Username,
    string[] Roles,
    string[] Scopes,
    string? Avatar);
