using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FdroidMgr.Api.Validation;
using FluentAssertions;

namespace FdroidMgr.Tests.Unit.Services;

public class ScopesTests
{
    [Fact]
    public void Expand_AddsTheImpliedReadAndKeepsCanonicalOrder()
    {
        Scopes.Expand(["releases:write"]).Should().Equal(Scopes.Read, Scopes.ReleasesWrite);
        Scopes.Expand(["apps:write", "read"]).Should().Equal(Scopes.Read, Scopes.AppsWrite);
        // unknown scopes are dropped silently
        Scopes.Expand(["bogus", "read"]).Should().Equal(Scopes.Read);
        Scopes.Expand(["bogus"]).Should().BeEmpty();
        Scopes.Expand([]).Should().BeEmpty();
    }

    [Fact]
    public void Validator_AcceptsExactlyTheKnownScopes()
    {
        var validator = new CreateApiKeyRequestValidator();

        foreach (var scope in Scopes.All)
        {
            Scopes.IsValid(scope).Should().BeTrue();
            validator.Validate(new CreateApiKeyRequest { Name = "k", Scopes = [scope] }).IsValid
                .Should().BeTrue($"{scope} is a scope the policies can require");
        }

        Scopes.IsValid("admin").Should().BeFalse();
        validator.Validate(new CreateApiKeyRequest { Name = "k", Scopes = ["admin"] }).IsValid
            .Should().BeFalse();
    }
}
