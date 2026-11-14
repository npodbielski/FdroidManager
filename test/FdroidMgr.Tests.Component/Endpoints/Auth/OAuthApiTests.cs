using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Auth;

public class OAuthApiTests
{
    private const string MasterKey = "api-test-master-key-0123456789abcdef";

    [Fact]
    public async Task OAuthStatus_Anonymous_ReportsUnconfigured()
    {
        var (root, factory, client) = BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.GetAsync("/api/auth/oauth/status");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("configured").GetBoolean().Should().BeFalse();
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ConfigureOAuth_MissingAuthority_Returns400()
    {
        var (root, factory, client) = BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.PostAsync("/api/auth/oauth", Json(new { clientId = "fdroidmgr" }));
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("authority");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ConfigureOAuth_HttpWithoutAllowInsecure_Returns400()
    {
        var (root, factory, client) = BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.PostAsync("/api/auth/oauth", Json(new
            {
                authority = "http://localhost:8084",
                clientId = "fdroidmgr",
            }));
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("https");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ConfigureOAuth_Valid_Returns201_WritesPublicClientConfig()
    {
        var (root, factory, client) = BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.PostAsync("/api/auth/oauth", Json(ValidRequest()));
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var raw = await res.Content.ReadAsStringAsync();
            raw.Should().NotContain("clientSecret");

            var body = JsonDocument.Parse(raw).RootElement;
            body.GetProperty("restartRequired").GetBoolean().Should().BeFalse();
            body.GetProperty("authority").GetString().Should().Be("https://id.example.com/realms/fdroid");
            body.GetProperty("clientId").GetString().Should().Be("fdroidmgr");
            body.GetProperty("scopes").EnumerateArray().Select(e => e.GetString())
                .Should().Contain(["openid", "profile", "email"]);

            var config = File.ReadAllText(Path.Combine(root, TestHost.ConfigFileName));
            config.Should().NotContain("OAuthClientSecret");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ConfigureOAuth_SecondCall_Returns409()
    {
        var (root, factory, client) = BootAsync();
        await using var _ = factory;
        try
        {
            var first = await client.PostAsync("/api/auth/oauth", Json(ValidRequest()));
            first.StatusCode.Should().Be(HttpStatusCode.Created);

            var second = await client.PostAsync("/api/auth/oauth", Json(ValidRequest()));
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("already configured");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ConfigureOAuth_AlreadyConfiguredAtBoot_Returns409()
    {
        var (root, factory, client) = BootAsync(issuer: "https://id.example.com/realms/fdroid");
        await using var _ = factory;
        try
        {

            var status = await client.GetAsync("/api/auth/oauth/status");
            JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement
                .GetProperty("configured").GetBoolean().Should().BeTrue();

            var res = await client.PostAsync("/api/auth/oauth", Json(ValidRequest()));
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("already configured");
            body.GetProperty("authority").GetString().Should().Be("https://id.example.com/realms/fdroid");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task LocalLogin_IsRemoved()
    {
        var (root, factory, client) = BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.PostAsync("/api/auth/login", Json(new { username = "admin", password = "admin" }));

            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Login_Unconfigured_RedirectsToUi_Not500()
    {
        var (root, factory, _) = BootAsync();
        await using var _ = factory;
        try
        {
            using var noRedirect = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
            var res = await noRedirect.GetAsync("/api/auth/login");
            res.StatusCode.Should().Be(HttpStatusCode.Redirect);
            res.Headers.Location!.ToString().Should().EndWith("/ui");
        }
        finally { Cleanup(root); }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static (string Root, WebApplicationFactory<Program> Factory, HttpClient Client) BootAsync(string? issuer = null)
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "oauth.db",
            MasterKey = MasterKey,
            TestAuth = false,
            DirPrefix = "fdroidmgr-oauthapi",
            Settings = issuer is null
                ? null
                : new Dictionary<string, string?> { ["Auth:OAuthIssuer"] = issuer },
        });
        return (host.Root, host.Factory, host.Client());
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private static object ValidRequest() => new
    {
        authority = "https://id.example.com/realms/fdroid",
        clientId = "fdroidmgr",
        scopes = new[] { "openid", "email" },
    };
}
