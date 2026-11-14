using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FdroidMgr.Api;

public static class OidcAuthExtensions
{
    public static string ResolveConfigFile(IConfiguration config)
        => config["FDROID_CONFIG_FILE"] ?? "/config/appsettings.json";

    public static void AddOidcConfigFile(this WebApplicationBuilder builder)
    {
        var path = ResolveConfigFile(builder.Configuration);

        SeedConfigFile(path);
        builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
        if (builder.Configuration is IConfigurationRoot root)
        {
            builder.Services.AddSingleton(root);
        }
    }

    internal static void SeedConfigFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return;
            }

            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = Path.Combine(dir ?? ".", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.seed.tmp");
            try
            {
                File.WriteAllText(temp, "{}\n");
                try
                {

                    File.Move(temp, full);
                }
                catch (IOException) when (File.Exists(full))
                {

                }
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARN: could not seed config file {path}: {ex}");
        }
    }

    public static void AddOidcProvider(this AuthenticationBuilder builder, string? publicBaseUrl)
    {
        builder.AddOpenIdConnect(o =>
        {
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.ResponseMode = OpenIdConnectResponseMode.Query;
            o.UsePkce = true;
            o.SaveTokens = false;
            o.CallbackPath = "/signin-oidc";
            o.SignedOutCallbackPath = "/signout-oidc";
            o.GetClaimsFromUserInfoEndpoint = true;

            o.ClaimActions.MapUniqueJsonKey("picture", "picture");

            o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.CorrelationCookie.SameSite = SameSiteMode.Lax;
            o.NonceCookie.SameSite = SameSiteMode.Lax;

            if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                var callbackUrl = publicBaseUrl + "/signin-oidc";
                o.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    ctx.ProtocolMessage.RedirectUri = callbackUrl;
                    return Task.CompletedTask;
                };
            }

            o.Events.OnRemoteFailure = ctx =>
            {
                ctx.HttpContext.RequestServices.GetService<ISignInDiagnostics>()?.Record(ctx.Failure);

                ctx.HandleResponse();
                if (WantsHtmlPage(ctx.Request))
                {

                    ctx.Response.Redirect(SignInFailureTarget(ctx.Properties));
                    return Task.CompletedTask;
                }

                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("""{"error":"sign-in failed"}""");
            };

            o.Events.OnTokenValidated = ctx =>
            {
                ctx.HttpContext.RequestServices.GetService<ISignInDiagnostics>()?.Clear();
                return Task.CompletedTask;
            };

            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");
            o.Scope.Add("email");

            o.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "preferred_username",
                RoleClaimType = "roles",
            };
        });

        builder.Services.Configure<AuthenticationOptions>(o =>
        {
            var scheme = o.Schemes.FirstOrDefault(s => s.Name == OpenIdConnectDefaults.AuthenticationScheme);
            if (scheme != null)
            {
                scheme.HandlerType = typeof(DynamicOpenIdConnectHandler);
            }
        });

        builder.Services.RemoveAll<IValidateOptions<OpenIdConnectOptions>>();

        builder.Services.AddOptions<OidcProviderOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            o.Issuer = cfg["Auth:OAuthIssuer"];
            o.ClientId = cfg["Auth:OAuthClientId"];
            o.Scopes = cfg["Auth:OAuthScopes"] ?? "";
        });
    }

    public static string SignInSuccessTarget(string? returnUrl)
        => SiteRelative(returnUrl) ?? "/ui";

    internal static string SignInFailureTarget(AuthenticationProperties? properties)
        => SiteRelative(properties?.RedirectUri, except: "/ui") ?? "/ui.html";

    private static string? SiteRelative(string? url, string? except = null)
        => url is { Length: > 0 } && url != except
            && url.StartsWith('/')
            && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.Contains('\\')
            ? url
            : null;

    internal static bool WantsHtmlPage(HttpRequest request)
        => request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
}
