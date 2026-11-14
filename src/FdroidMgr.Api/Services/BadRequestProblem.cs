using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

/// <summary>
/// A request the caller can correct by sending something else.
/// </summary>
public sealed class BadRequestProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(ArgumentException);

    public void Apply(ProblemDetailsContext context, Exception exception)
        => ProblemResponder.Reply(context, StatusCodes.Status400BadRequest, "Bad Request", exception.Message);
}
