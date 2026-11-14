using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class NotFoundProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(KeyNotFoundException);

    public void Apply(ProblemDetailsContext context, Exception exception)
        => ProblemResponder.Reply(context, StatusCodes.Status404NotFound, "Not Found", exception.Message);
}
