using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FdroidMgr.Tests.Component.Fixtures;

public sealed class FakeOidcServer : IAsyncDisposable
{
    public const string ClientId = "fdroidmgr";
    public const string UserName = "alice";
    public const string Picture = "https://id.example.com/avatars/alice.png";

    private readonly WebApplication _app;
    private readonly bool _emitPicture;
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _kid = "fake-kid-" + Guid.NewGuid().ToString("N")[..8];
    private readonly ConcurrentDictionary<string, (string Nonce, string? CodeChallenge)> _codes = new();
    private readonly ConcurrentDictionary<string, string> _tokens = new();

    // Keycloak advertises PAR but refuses it for a public client; a handler that pushes
    // anyway fails the challenge before the browser reaches the provider.
    public int ParRequestCount { get; private set; }


    private FakeOidcServer(WebApplication app, bool emitPicture) => (_app, _emitPicture) = (app, emitPicture);

    public string Authority { get; private set; } = "";

    public static async Task<FakeOidcServer> StartAsync(bool emitPicture = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var server = new FakeOidcServer(app, emitPicture);
        server.MapEndpoints();
        await app.StartAsync();

        server.Authority = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return server;
    }

    private static string Issuer(HttpContext ctx)
        => $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}".TrimEnd('/');

    private void MapEndpoints()
    {
        _app.MapGet("/.well-known/openid-configuration", (HttpContext ctx) =>
        {
            var iss = Issuer(ctx);
            return Results.Json(new
            {
                issuer = iss,
                authorization_endpoint = iss + "/authorize",
                token_endpoint = iss + "/token",
                userinfo_endpoint = iss + "/userinfo",
                jwks_uri = iss + "/jwks",
                pushed_authorization_request_endpoint = iss + "/par",
                require_pushed_authorization_requests = false,
                id_token_signing_alg_values_supported = new[] { "RS256" },
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" },
            });
        });

        _app.MapGet("/jwks", () =>
        {
            var p = _rsa.ExportParameters(false);
            return Results.Json(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = _kid,
                        alg = "RS256",
                        n = Base64Url(p.Modulus!),
                        e = Base64Url(p.Exponent!),
                    },
                },
            });
        });

        _app.MapPost("/par", (HttpContext ctx) =>
        {
            ParRequestCount++;
            var authz = ctx.Request.Headers.Authorization.ToString();
            if (!authz.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "invalid_request", error_description = "Authentication failed." }, statusCode: 401);
            }

            throw new InvalidOperationException("a public client must not push authorization requests");
        });

        _app.MapGet("/authorize", (HttpContext ctx) =>
        {
            var q = ctx.Request.Query;
            var redirect = q["redirect_uri"].ToString();
            var state = q["state"].ToString();
            var nonce = q["nonce"].ToString();
            var code = "code-" + Guid.NewGuid().ToString("N");
            _codes[code] = (nonce, q["code_challenge"].ToString());

            var sep = redirect.Contains('?') ? "&" : "?";
            return Results.Redirect(redirect + sep
                + "code=" + Uri.EscapeDataString(code)
                + "&state=" + Uri.EscapeDataString(state));
        });

        _app.MapPost("/token", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var code = form["code"].ToString();
            var clientId = form["client_id"].ToString();

            if (clientId != ClientId)
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            }

            if (!_codes.TryRemove(code, out var entry))
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            var verifier = form["code_verifier"].ToString();
            if (string.IsNullOrEmpty(entry.CodeChallenge)
                || string.IsNullOrEmpty(verifier)
                || Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != entry.CodeChallenge)
            {
                return Results.Json(new { error = "invalid_grant", error_description = "PKCE verification failed" }, statusCode: 400);
            }

            var iss = Issuer(ctx);
            var accessToken = "access-" + Guid.NewGuid().ToString("N");
            _tokens[accessToken] = "1";

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var idToken = SignJwt(new Dictionary<string, object>
            {
                ["iss"] = iss,
                ["sub"] = "1",
                ["aud"] = clientId,
                ["exp"] = now + 3600,
                ["iat"] = now,
                ["nonce"] = entry.Nonce,
                ["preferred_username"] = UserName,
            });

            return Results.Json(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = 3600,
                id_token = idToken,
            });
        });

        _app.MapGet("/userinfo", (HttpContext ctx) =>
        {
            var authz = ctx.Request.Headers.Authorization.ToString();
            var token = authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authz["Bearer ".Length..].Trim()
                : "";
            if (!_tokens.ContainsKey(token))
            {
                return Results.StatusCode(401);
            }

            if (_emitPicture)
            {
                return Results.Json(new
                {
                    sub = "1",
                    preferred_username = UserName,
                    name = "Alice",
                    email = "alice@example.com",
                    picture = Picture,
                });
            }

            return Results.Json(new
            {
                sub = "1",
                preferred_username = UserName,
                name = "Alice",
                email = "alice@example.com",
            });
        });
    }

    private string SignJwt(Dictionary<string, object> payload)
    {
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["kid"] = _kid, ["typ"] = "JWT" };
        var h = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var sig = _rsa.SignData(Encoding.ASCII.GetBytes(h + "." + p), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return h + "." + p + "." + Base64Url(sig);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _rsa.Dispose();
    }
}
