using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class ReleaseConflictProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(ReleaseConflictException);

    public void Apply(ProblemDetailsContext context, Exception exception)
        => ProblemResponder.Reply(context, StatusCodes.Status409Conflict, "Conflict", exception.Message);
}
