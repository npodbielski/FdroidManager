using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Auth;

public class OAuthLoginIntegrationTests
{
    private const string MasterKey = "flow-master-key-0123456789abcdef";

    private const string BrowserAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    private const string DeadAuthority = "http://127.0.0.1:1";

    [Fact]
    public async Task SignInFailure_IsReportedByTheStatusEndpoint_UntilASignInSucceeds()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        var root = Root("fdroidmgr-errline");
        using var host = new TestHost(new TestHostOptions
        {
            Root = root,
            DbName = "errline.db",
            MasterKey = MasterKey,
            TestAuth = false,
            Services = DataProtectionIn(root),
        });
        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));
        var config = Path.Combine(root, TestHost.ConfigFileName);

        var saved = await app.PostAsync("/api/auth/oauth", new StringContent(
            JsonSerializer.Serialize(new
            {
                authority = idp.Authority,
                clientId = FakeOidcServer.ClientId,
                allowInsecure = true,
            }), Encoding.UTF8, "application/json"));
        saved.StatusCode.Should().Be(HttpStatusCode.Created);

        await CompleteSignInAsync(app, idp.Authority);
        (await SignInErrorAsync(app)).Should().BeNull("nothing has failed yet");

        File.WriteAllText(config, File.ReadAllText(config).Replace(idp.Authority, DeadAuthority));
        var bounced = await PollForAsync(async () =>
        {
            try
            {
                var nav = await app.GetAsync("/api/auth/login", BrowserAccept);
                return nav.Headers.Location?.ToString() == "/ui.html" ? nav : null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        });
        bounced.Should().NotBeNull("the challenge must fail once the issuer is unreachable");

        var error = await SignInErrorAsync(app);
        error.Should().NotBeNullOrWhiteSpace("the sign-in screen has to say why it failed");
        error.Should().Contain("127.0.0.1", "the line carries the reason, not just a label");

        File.WriteAllText(config, File.ReadAllText(config).Replace(DeadAuthority, idp.Authority));
        await app.DeleteAsync("/api/auth/logout");
        await CompleteSignInAsync(app, idp.Authority);

        (await SignInErrorAsync(app)).Should().BeNull("a success retires the earlier cause");
    }

    private sealed class CookieJarClient(HttpClient http, Uri @base) : IDisposable
    {
        private readonly List<string> _cookies = [];

        public async Task<HttpResponseMessage> GetAsync(string url, string? accept = null)
            => await SendAsync(HttpMethod.Get, url, null, accept);

        public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
            => await SendAsync(HttpMethod.Post, url, content);

        public async Task DeleteAsync(string url)
        {
            await SendAsync(HttpMethod.Delete, url, null);
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, string? accept = null)
        {
            var request = new HttpRequestMessage(method, new Uri(@base, url)) { Content = content };
            if (accept is not null)
            {
                request.Headers.TryAddWithoutValidation("Accept", accept);
            }

            if (_cookies.Count > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies));
            }

            using var req = request;
            var resp = await http.SendAsync(req);
            if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var sc in setCookies)
                {
                    var pair = sc.Split(';', 2)[0].Trim();
                    var name = pair.Split('=', 2)[0];
                    _cookies.RemoveAll(c => c.StartsWith(name + "=", StringComparison.Ordinal));
                    _cookies.Add(pair);
                }
            }
            return resp;
        }

        public void Dispose() => http.Dispose();
    }

    [Fact]
    public async Task OidcLogin_FullRedirectFlow_EstablishesSessionCookie_AndReturnsUser()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(
            host.Client(handleCookies: false),
            new Uri("http://localhost"));
        using var idpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var challenge = await app.GetAsync("/api/auth/login");
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var authorize = challenge.Headers.Location!;
        authorize.GetLeftPart(UriPartial.Authority).Should().Be(idp.Authority);

        var q = QueryHelpers.ParseQuery(authorize.Query);
        q["response_type"].ToString().Should().Be("code");
        q["client_id"].ToString().Should().Be(FakeOidcServer.ClientId);
        q["redirect_uri"].ToString().Should().Be("http://localhost/signin-oidc");
        q["code_challenge_method"].ToString().Should().Be("S256");
        q["code_challenge"].ToString().Should().NotBeNullOrEmpty();
        q["nonce"].ToString().Should().NotBeNullOrEmpty();
        q["state"].ToString().Should().NotBeNullOrEmpty();
        q["scope"].ToString().Should().Contain("openid").And.Contain("profile");
        q["scope"].ToString().Should().Contain("email");

        var idpResp = await idpClient.GetAsync(authorize);
        idpResp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var callback = idpResp.Headers.Location!;
        callback.AbsolutePath.Should().Be("/signin-oidc");

        var cb = await app.GetAsync(callback.ToString());
        cb.Headers.TryGetValues("Set-Cookie", out var cookies);
        cookies.Should().Contain(c => c.StartsWith(".FdroidMgr.Auth=", StringComparison.Ordinal));

        var me = await app.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement;
        meJson.GetProperty("username").GetString().Should().Be(FakeOidcServer.UserName);
        meJson.GetProperty("avatar").GetString().Should().Be(FakeOidcServer.Picture);
    }

    [Fact]
    public async Task OidcLogin_IdPWithoutPicture_MeReturnsNullAvatar()
    {
        await using var idp = await FakeOidcServer.StartAsync(emitPicture: false);
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(
            host.Client(handleCookies: false),
            new Uri("http://localhost"));
        using var idpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var challenge = await app.GetAsync("/api/auth/login");
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var idpResp = await idpClient.GetAsync(challenge.Headers.Location!);
        idpResp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        await app.GetAsync(idpResp.Headers.Location!.ToString());

        var me = await app.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement;
        meJson.GetProperty("username").GetString().Should().Be(FakeOidcServer.UserName);
        meJson.GetProperty("avatar").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task OidcConfig_SavedAtRuntime_GoesLive_WithoutRestart()
    {
        await using var idp = await FakeOidcServer.StartAsync();

        var root = Root("fdroidmgr-oauthnorst");
        var host = new TestHost(new TestHostOptions
        {
            Root = root,
            DbName = "norst.db",
            MasterKey = MasterKey,
            TestAuth = false,
            Services = DataProtectionIn(root),
        });
        using var _ = host;

        using var app = new CookieJarClient(
            host.Client(handleCookies: false),
            new Uri("http://localhost"));
        using var idpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var unconfigured = await app.GetAsync("/api/auth/login");
        unconfigured.StatusCode.Should().Be(HttpStatusCode.Redirect);
        unconfigured.Headers.Location!.ToString().Should().EndWith("/ui");

        var saved = await app.PostAsync("/api/auth/oauth", new StringContent(
            JsonSerializer.Serialize(new
            {
                authority = idp.Authority,
                clientId = FakeOidcServer.ClientId,
                allowInsecure = true,
            }), Encoding.UTF8, "application/json"));
        saved.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement
            .GetProperty("restartRequired").GetBoolean().Should().BeFalse();

        Uri? authorize = null;
        for (var i = 0; i < 400; i++)
        {
            var login = await app.GetAsync("/api/auth/login");
            var loc = login.Headers.Location?.ToString() ?? "";
            if (loc.StartsWith(idp.Authority, StringComparison.OrdinalIgnoreCase))
            {
                authorize = login.Headers.Location!;
                break;
            }
            await Task.Delay(50);
        }
        authorize.Should().NotBeNull("sign-in should go live after saving, without a restart");

        var idpResp = await idpClient.GetAsync(authorize);
        idpResp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var cb = await app.GetAsync(idpResp.Headers.Location!.ToString());
        cb.Headers.TryGetValues("Set-Cookie", out var cookies);
        cookies.Should().Contain(c => c.StartsWith(".FdroidMgr.Auth=", StringComparison.Ordinal));

        var me = await app.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement
            .GetProperty("username").GetString().Should().Be(FakeOidcServer.UserName);
    }

    [Fact]
    public async Task StaleCallback_BrowserNavigation_RedirectsToUiHtml()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));

        var stale = await app.GetAsync(
            "/signin-oidc?code=already-consumed&state=also-consumed",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        stale.StatusCode.Should().Be(HttpStatusCode.Redirect);
        stale.Headers.Location!.ToString().Should().Be("/ui.html");
        stale.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).Should().NotContain(c => c.StartsWith(".FdroidMgr.Auth=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaleCallback_NonBrowserClient_KeepsJsonError()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));

        var stale = await app.GetAsync("/signin-oidc?code=already-consumed&state=also-consumed", "application/json");

        stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        stale.Headers.Location.Should().BeNull();
        JsonDocument.Parse(await stale.Content.ReadAsStringAsync()).RootElement
            .GetProperty("error").GetString().Should().NotBeNullOrEmpty();

        (await app.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PopupSignIn_LandsOnItsOwnPage_WithTheSessionEstablished()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));

        var challenge = await app.GetAsync("/api/auth/login?returnUrl=%2Flogin-done.html");
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var callback = await CompleteCallbackAsync(app, challenge.Headers.Location!);
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().Be("/login-done.html");

        var me = await app.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PopupLandingPage_IsServedAnonymously()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        var page = await host.Client().GetAsync("/login-done.html");

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).Should().Contain("fdroidmgr:signed-in");
    }

    [Fact]
    public async Task Challenge_CorrelationAndNonceCookiesAreStorableOverPlainHttp()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        var challenge = await host.Client(handleCookies: false).GetAsync("/api/auth/login");

        challenge.StatusCode.Should().Be(HttpStatusCode.Found);
        var flow = challenge.Headers.GetValues("Set-Cookie")
            .Where(c => c.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal)
                || c.StartsWith(".AspNetCore.OpenIdConnect.Nonce.", StringComparison.Ordinal))
            .ToList();

        flow.Should().HaveCount(2);
        flow.Should().OnlyContain(c => !c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        flow.Should().OnlyContain(c => !c.Contains("samesite=none", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SignIn_AbsoluteReturnUrl_IsNotHonoured()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));

        var challenge = await app.GetAsync("/api/auth/login?returnUrl=" + Uri.EscapeDataString("https://evil.example/"));
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var callback = await CompleteCallbackAsync(app, challenge.Headers.Location!);
        callback.Headers.Location!.ToString().Should().Be("/ui");
    }

    [Fact]
    public async Task PopupSignIn_FailedExchange_StaysOnThePopupLandingPage()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));

        var challenge = await app.GetAsync("/api/auth/login?returnUrl=%2Flogin-done.html");
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();

        var failed = await app.GetAsync(
            "/signin-oidc?code=never-issued&state=" + Uri.EscapeDataString(state),
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        failed.StatusCode.Should().Be(HttpStatusCode.Redirect);
        failed.Headers.Location!.ToString().Should().Be("/login-done.html");
        (await app.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Challenge_IdpUnreachable_RedirectsTheBrowserToSignIn()
    {
        using var host = BootHost("http://127.0.0.1:1");

        using var app = new CookieJarClient(host.Client(handleCookies: false), new Uri("http://localhost"));

        var nav = await app.GetAsync("/api/auth/login", BrowserAccept);
        nav.StatusCode.Should().Be(HttpStatusCode.Redirect);
        nav.Headers.Location!.ToString().Should().Be("/ui.html");

        var popup = await app.GetAsync("/api/auth/login?returnUrl=%2Flogin-done.html", BrowserAccept);
        popup.StatusCode.Should().Be(HttpStatusCode.Redirect);
        popup.Headers.Location!.ToString().Should().Be("/login-done.html");

        (await app.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OidcLogin_InvalidCode_DoesNotAuthenticate()
    {
        await using var idp = await FakeOidcServer.StartAsync();
        using var host = BootHost(idp.Authority);

        using var app = new CookieJarClient(
            host.Client(handleCookies: false),
            new Uri("http://localhost"));

        try { await app.GetAsync("/signin-oidc?code=does-not-exist&state=whatever"); }
        catch (HttpRequestException) { }

        var me = await app.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        me.Headers.Location.Should().BeNull();
    }

    private static async Task CompleteSignInAsync(CookieJarClient app, string authority)
    {
        var authorize = await PollForAsync(async () =>
        {
            try
            {
                var login = await app.GetAsync("/api/auth/login");
                var loc = login.Headers.Location;
                return loc is not null && loc.ToString().StartsWith(authority, StringComparison.OrdinalIgnoreCase)
                    ? loc
                    : null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        });
        authorize.Should().NotBeNull($"sign-in should reach the provider at {authority}");

        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var back = await idp.GetAsync(authorize);
        back.StatusCode.Should().Be(HttpStatusCode.Redirect);
        await app.GetAsync(back.Headers.Location!.ToString());
        (await app.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<string?> SignInErrorAsync(CookieJarClient app)
    {
        var status = await app.GetAsync("/api/auth/oauth/status");
        var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("lastSignInError", out var err)
            && err.ValueKind == JsonValueKind.String
                ? err.GetString()
                : null;
    }

    private static async Task<T?> PollForAsync<T>(Func<Task<T?>> probe, int rounds = 400)
        where T : class
    {
        for (var i = 0; i < rounds; i++)
        {
            var found = await probe();
            if (found is not null)
            {
                return found;
            }
            await Task.Delay(50);
        }
        return null;
    }

    private static TestHost BootHost(string authority)
    {
        var root = Root("fdroidmgr-oidcflow");
        return new TestHost(new TestHostOptions
        {
            Root = root,
            DbName = "flow.db",
            MasterKey = MasterKey,

            TestAuth = false,
            Settings = new Dictionary<string, string?>
            {
                ["Auth:OAuthIssuer"] = authority,
                ["Auth:OAuthClientId"] = FakeOidcServer.ClientId,
            },
            Services = DataProtectionIn(root),
        });
    }

    private static string Root(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static Action<IServiceCollection> DataProtectionIn(string root)
        => services => services
            .AddDataProtection()
            .SetApplicationName(Guid.NewGuid().ToString("N"))
            .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(root, "dp-keys")));

    private static async Task<HttpResponseMessage> CompleteCallbackAsync(CookieJarClient app, Uri authorize)
    {
        using var idp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var redirected = await idp.GetAsync(authorize);
        redirected.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return await app.GetAsync(redirected.Headers.Location!.ToString());
    }
}
