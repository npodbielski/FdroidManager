using FdroidMgr.Api.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace FdroidMgr.Api;

/// <summary>
/// Plugs the per-exception problem mappers into Problem Details, so endpoints do
/// not hand-copy try/catch blocks.
/// </summary>
public sealed class ApiProblemDetails(IEnumerable<IExceptionProblemMapper> mappers)
    : IConfigureOptions<ProblemDetailsOptions>
{
    public void Configure(ProblemDetailsOptions options)
        => options.CustomizeProblemDetails = new ProblemMapperMap(mappers).Apply;

    /// <summary>
    /// Routes an exception to the mapper registered for its exact type, or to the
    /// closest registered base type when nothing matches it exactly.
    /// </summary>
    private sealed class ProblemMapperMap
    {
        private readonly Dictionary<Type, IExceptionProblemMapper> _byType = [];

        private readonly IExceptionProblemMapper _fallback;

        public ProblemMapperMap(IEnumerable<IExceptionProblemMapper> mappers)
        {
            foreach (var mapper in mappers)
            {
                _byType[mapper.Handles] = mapper;
            }

            if (!_byType.TryGetValue(typeof(Exception), out var fallback))
            {
                throw new InvalidOperationException("an Exception fallback problem mapper is required");
            }

            _fallback = fallback;
        }

        public void Apply(ProblemDetailsContext ctx)
        {
            var exception = ctx.Exception
                ?? ctx.HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error;

            // A body the endpoint shaped itself (request validation) needs no mapping.
            if (exception is null)
            {
                return;
            }

            Resolve(exception.GetType()).Apply(ctx, exception);
        }

        private IExceptionProblemMapper Resolve(Type type)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                if (_byType.TryGetValue(t, out var mapper))
                {
                    return mapper;
                }
            }

            return _fallback;
        }
    }
}
