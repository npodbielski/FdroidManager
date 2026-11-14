
namespace FdroidMgr.Api.Services;

/// <summary>
/// Shared rendering: a Problem Details document that also carries a top-level
/// <c>error</c> field, which is what the Web UI reads.
/// </summary>
internal static class ProblemResponder
{
    public static void Reply(ProblemDetailsContext context, int status, string title, string? error)
    {
        var problem = context.ProblemDetails;
        problem.Status = status;
        problem.Title = title;
        problem.Detail = error;
        problem.Extensions["error"] = error ?? title;
        context.HttpContext.Response.StatusCode = status;
    }
}
