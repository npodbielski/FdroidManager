using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

/// <summary>
/// Last resort, and the only mapper that does not echo the exception: a message
/// nobody asked for is not ours to publish.
/// </summary>
public sealed class UnhandledProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(Exception);

    public void Apply(ProblemDetailsContext context, Exception exception)
        => ProblemResponder.Reply(context, StatusCodes.Status500InternalServerError,
            "An error occurred while processing the request", null);
}
