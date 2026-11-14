using System.Text.Encodings.Web;
using FdroidMgr.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using FdroidMgr.Api.Abstractions;
namespace FdroidMgr.Api.Services;

public sealed partial class DynamicOpenIdConnectHandler(
    IOptionsMonitor<OpenIdConnectOptions> options,
    ILoggerFactory logger,
    HtmlEncoder htmlEncoder,
    UrlEncoder encoder,
    IOptionsSnapshot<OidcProviderOptions> provider,
    ISignInDiagnostics diagnostics)
    : OpenIdConnectHandler(options, logger, htmlEncoder, encoder)
{
    private readonly OidcProviderOptions _provider = provider.Get(null);
    private readonly ILogger<DynamicOpenIdConnectHandler> _log = logger.CreateLogger<DynamicOpenIdConnectHandler>();
    private bool _providerLive;

    protected override Task InitializeHandlerAsync()
    {
        _providerLive = ApplyLiveProvider();
        return Task.CompletedTask;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!_providerLive)
        {
            Context.Response.Redirect("/ui");
            return;
        }

        try
        {
            await base.HandleChallengeAsync(properties);
        }
        catch (Exception ex)
        {
            Log.OidcSignInFailed(_log, ex, _provider.Issuer);
            diagnostics.Record(ex);
            if (!Context.Response.HasStarted && OidcAuthExtensions.WantsHtmlPage(Context.Request))
            {
                Context.Response.Redirect(OidcAuthExtensions.SignInFailureTarget(properties));
                return;
            }
            throw;
        }
    }

    public override Task<bool> HandleRequestAsync()
        => _providerLive ? base.HandleRequestAsync() : Task.FromResult(false);

    public override Task SignOutAsync(AuthenticationProperties? properties)
        => _providerLive ? base.SignOutAsync(properties) : Task.CompletedTask;

    private bool ApplyLiveProvider()
    {
        var issuer = _provider.Issuer;
        var clientId = _provider.ClientId;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        try
        {
            if (Options.Authority != issuer)
            {
                Options.Authority = issuer;
                Options.RequireHttpsMetadata =
                    Uri.TryCreate(issuer, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
                Options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    issuer.TrimEnd('/') + "/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever(Backchannel) { RequireHttps = Options.RequireHttpsMetadata });
                Log.OidcProviderLive(_log, issuer);
            }

            if (Options.ClientId != clientId)
            {
                Options.ClientId = clientId;
                Options.TokenValidationParameters.ValidAudience = clientId;
            }

            var wanted = new List<string> { "openid", "profile", "email" };
            foreach (var s in _provider.Scopes
                     .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!wanted.Contains(s))
                {
                    wanted.Add(s);
                }
            }

            if (Options.Scope.SequenceEqual(wanted))
            {
                return true;
            }

            if (Options.Scope is not { } scope)
            {
                return true;
            }

            scope.Clear();
            foreach (var s in wanted)
            {
                scope.Add(s);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.OidcConfigInvalid(_log, ex);
            return false;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 22, Level = LogLevel.Error,
            Message = "OIDC sign-in request failed for {Issuer}")]
        public static partial void OidcSignInFailed(ILogger logger, Exception ex, string? issuer);

        [LoggerMessage(EventId = 23, Level = LogLevel.Information,
            Message = "OIDC provider live from config: {Issuer}")]
        public static partial void OidcProviderLive(ILogger logger, string issuer);

        [LoggerMessage(EventId = 24, Level = LogLevel.Warning,
            Message = "OIDC config is invalid — provider disabled until fixed")]
        public static partial void OidcConfigInvalid(ILogger logger, Exception ex);
    }
}
