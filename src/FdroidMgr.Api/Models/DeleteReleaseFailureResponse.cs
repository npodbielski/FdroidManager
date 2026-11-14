namespace FdroidMgr.Api.Models;

/// <summary>
/// Failed <c>fdroid update</c> after a release delete: the APK is gone but the
/// index may be stale, and the caller needs to know how many duplicates a
/// rebuild attempt cleaned up first.
/// </summary>
public record DeleteReleaseFailureResponse(string Error, int DuplicatesRemoved);
