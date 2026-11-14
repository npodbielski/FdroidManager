using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService keys)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string KeyPrefix = "fdmgr_";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = ExtractSecret(Request);
        if (secret is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (!secret.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("invalid API key");
        }

        var principal = await keys.ValidateAsync(secret, Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("invalid or expired API key");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.Subject),
            new(ClaimTypes.Name, principal.Username),
            new("sub", principal.Subject),
            new("api_key_id", principal.KeyId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("api_key_name", principal.KeyName),
        };
        foreach (var scope in principal.Scopes)
        {
            claims.Add(new Claim(Scopes.ClaimType, scope));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        return Response.WriteAsync("""{"error":"unauthorized"}""");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        return Response.WriteAsync("""{"error":"forbidden"}""");
    }

    private static string? ExtractSecret(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer".Length..].Trim();
        }

        var apiKey = request.Headers["X-API-Key"].ToString();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey.Trim();
        }

        if (authorization.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = authorization["Basic".Length..].Trim();
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var colon = decoded.IndexOf(':');
                return (colon >= 0 ? decoded[(colon + 1)..] : decoded).Trim();
            }
            catch (FormatException)
            {
                return null;
            }
        }

        var queryKey = request.Query["key"].ToString();
        return string.IsNullOrEmpty(queryKey) ? null : queryKey.Trim();
    }
}
