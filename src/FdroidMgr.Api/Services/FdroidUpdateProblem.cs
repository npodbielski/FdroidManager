using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class FdroidUpdateProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(FdroidUpdateException);

    public void Apply(ProblemDetailsContext context, Exception exception)
        => ProblemResponder.Reply(context, StatusCodes.Status502BadGateway, "Upstream command failed", exception.Message);
}
