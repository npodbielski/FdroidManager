using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

/// <summary>
/// Bad input reported as a plain <see cref="InvalidOperationException"/>, which is
/// what the services throw when the request cannot be carried out as asked.
/// </summary>
public sealed class InvalidOperationProblem : IExceptionProblemMapper
{
    public Type Handles => typeof(InvalidOperationException);

    public void Apply(ProblemDetailsContext context, Exception exception)
        => ProblemResponder.Reply(context, StatusCodes.Status400BadRequest, "Bad Request", exception.Message);
}
