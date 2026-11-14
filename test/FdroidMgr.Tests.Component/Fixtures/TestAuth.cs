using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FdroidMgr.Tests.Component.Fixtures;

public static class TestAuth
{
    private const string Scheme = "Test";
    public const string Header = "X-Test-User";

    public static void ConfigureServices(IServiceCollection services)
        => services.AddAuthentication(o =>
            {
                o.DefaultScheme = Scheme;
                o.DefaultAuthenticateScheme = Scheme;
                o.DefaultChallengeScheme = Scheme;
                o.DefaultSignInScheme = Scheme;
                o.DefaultSignOutScheme = Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, _ => { });
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[TestAuth.Header].FirstOrDefault();
        if (string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("preferred_username", user),
            new("roles", "admin"),
            new(ClaimTypes.NameIdentifier, "test-sub-" + user),
            new("sub", "test-sub-" + user),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name, "preferred_username", "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
