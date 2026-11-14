using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Services;
using Microsoft.Extensions.Options;

namespace FdroidMgr.Api;

public static class ApiProblemDetailsRegistration
{
    /// <summary>
    /// Problem Details plus one mapper per exception the services throw. Adding a
    /// mapping means adding an <see cref="IExceptionProblemMapper"/> here; the
    /// dispatcher never changes.
    /// </summary>
    public static void AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddSingleton<IExceptionProblemMapper, NotFoundProblem>();
        services.AddSingleton<IExceptionProblemMapper, ReleaseConflictProblem>();
        services.AddSingleton<IExceptionProblemMapper, RepoAlreadyInitializedProblem>();
        services.AddSingleton<IExceptionProblemMapper, OAuthAlreadyConfiguredProblem>();
        services.AddSingleton<IExceptionProblemMapper, FdroidUpdateProblem>();
        services.AddSingleton<IExceptionProblemMapper, BadRequestProblem>();
        services.AddSingleton<IExceptionProblemMapper, InvalidOperationProblem>();
        services.AddSingleton<IExceptionProblemMapper, UnhandledProblem>();
        services.AddProblemDetails();
        services.AddSingleton<IConfigureOptions<ProblemDetailsOptions>, ApiProblemDetails>();
    }
}
