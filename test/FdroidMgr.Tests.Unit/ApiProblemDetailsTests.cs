using FdroidMgr.Api;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FdroidMgr.Tests.Unit;

public class ApiProblemDetailsTests
{
    [Fact]
    public void Apply_PicksTheMapperRegisteredForTheExactType()
    {
        var customize = Configure(new Marker(), new ForNotSupported());

        Apply(customize, new NotSupportedException())
            .ProblemDetails.Extensions["mapper"].Should().Be(nameof(ForNotSupported));
    }

    [Fact]
    public void Apply_WalksUpTheHierarchyWhenTheExactTypeIsNotRegistered()
    {
        var customize = Configure(new Marker(), new ForInvalidOperation());

        // FdroidUpdateException derives from InvalidOperationException.
        Apply(customize, new FdroidUpdateException("x"))
            .ProblemDetails.Extensions["mapper"].Should().Be(nameof(ForInvalidOperation));
    }

    [Fact]
    public void Apply_UsesTheExceptionFallbackForAnythingElse()
    {
        var customize = Configure(new Marker());

        Apply(customize, new Exception()).ProblemDetails.Extensions["mapper"].Should().Be(nameof(Marker));
    }

    [Fact]
    public void Apply_LeavesAProblemWithoutAnExceptionAlone()
    {
        var customize = Configure(new Marker());

        var ctx = Apply(customize, null);

        ctx.ProblemDetails.Extensions.Should().BeEmpty("a body an endpoint shaped itself is already correct");
    }

    [Fact]
    public void Configure_RequiresAFallbackForException()
    {
        var act = () => Configure(new ForInvalidOperation());

        act.Should().Throw<InvalidOperationException>().WithMessage("*fallback*");
    }

    private static Action<ProblemDetailsContext> Configure(params IExceptionProblemMapper[] mappers)
    {
        var options = new ProblemDetailsOptions();
        new ApiProblemDetails(mappers).Configure(options);
        return options.CustomizeProblemDetails!;
    }

    private static ProblemDetailsContext Apply(Action<ProblemDetailsContext> customize, Exception? exception)
    {
        var ctx = new ProblemDetailsContext
        {
            HttpContext = new DefaultHttpContext(),
            ProblemDetails = new ProblemDetails(),
            Exception = exception,
        };

        customize(ctx);

        return ctx;
    }

    private class Marker : IExceptionProblemMapper
    {
        public virtual Type Handles => typeof(Exception);

        public void Apply(ProblemDetailsContext context, Exception exception)
            => context.ProblemDetails.Extensions["mapper"] = GetType().Name;
    }

    private sealed class ForInvalidOperation : Marker
    {
        public override Type Handles => typeof(InvalidOperationException);
    }

    private sealed class ForNotSupported : Marker
    {
        public override Type Handles => typeof(NotSupportedException);
    }
}
