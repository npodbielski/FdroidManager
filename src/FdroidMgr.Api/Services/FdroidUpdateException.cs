namespace FdroidMgr.Api.Services;

/// <summary>
/// <c>fdroid</c> failed to do its job (non-zero exit, or it succeeded without the
/// side effect the caller needs). The HTTP layer maps it to 502: the request was
/// fine, the upstream command was not.
/// </summary>
public class FdroidUpdateException(string message) : InvalidOperationException(message);
