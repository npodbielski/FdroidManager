using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Services;

public sealed class RepoAlreadyInitializedProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(RepoAlreadyInitializedException);

    public void Apply(ProblemDetailsContext context, Exception exception)
    {
        ProblemResponder.Reply(context, StatusCodes.Status409Conflict, "Conflict", exception.Message);
        context.ProblemDetails.Extensions["backupPath"] = ((RepoAlreadyInitializedException)exception).BackupPath;
    }
}
