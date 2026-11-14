using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Services;

public sealed class OAuthAlreadyConfiguredProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(OAuthAlreadyConfiguredException);

    public void Apply(ProblemDetailsContext context, Exception exception)
    {
        ProblemResponder.Reply(context, StatusCodes.Status409Conflict, "Conflict", exception.Message);
        context.ProblemDetails.Extensions["authority"] = ((OAuthAlreadyConfiguredException)exception).Authority;
    }
}
