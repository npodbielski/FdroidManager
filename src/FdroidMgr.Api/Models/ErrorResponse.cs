namespace FdroidMgr.Api.Models;

/// <summary>
/// Body of a plain (non Problem Details) error answer: the Web UI reads the
/// top-level <c>error</c> field.
/// </summary>
public record ErrorResponse(string Error);
