namespace FdroidMgr.Api.Abstractions;

/// <summary>
/// One exception type and the problem body it maps to. Implement this once per
/// exception instead of extending a shared switch.
/// </summary>
public interface IExceptionProblemMapper
{
    /// <summary>
    /// The exception type handled here. A mapper registered for a derived type
    /// wins over one registered for its base.
    /// </summary>
    Type Handles { get; }

    /// <summary>
    /// Fills in <see cref="ProblemDetailsContext.ProblemDetails"/> and the response
    /// status for <paramref name="exception"/>.
    /// </summary>
    void Apply(ProblemDetailsContext context, Exception exception);
}
