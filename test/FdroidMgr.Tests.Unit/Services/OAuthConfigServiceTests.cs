using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class OAuthConfigServiceTests
{
    private const string MasterKey = "test-master-key-0123456789abcdef";

    [Fact]
    public void IsConfigured_FalseWhenNoIssuer()
    {
        var (svc, path) = Create(issuer: null);
        try { svc.IsConfigured().Should().BeFalse(); }
        finally { Cleanup(path); }
    }

    [Fact]
    public void IsConfigured_TrueWhenIssuerLoadedAtStartup()
    {
        var (svc, path) = Create(issuer: "https://id.example.com/realms/fdroid");
        try { svc.IsConfigured().Should().BeTrue(); }
        finally { Cleanup(path); }
    }

    [Fact]
    public void GetStatus_Unconfigured_ReportsConfiguredFalse()
    {
        var (svc, path) = Create(issuer: null);
        try
        {
            var s = svc.GetStatus();
            s.Configured.Should().BeFalse();
            s.Authority.Should().BeNull();
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData(null, null, "authority is required")]
    [InlineData("", "fdroidmgr", "authority is required")]
    [InlineData("not-a-url", "fdroidmgr", "authority must be a valid http(s) URL")]
    [InlineData("ftp://x", "fdroidmgr", "authority must be a valid http(s) URL")]
    [InlineData("https://id.example.com", null, "clientId is required")]
    public void Validate_RejectsBadInput(string? authority, string? clientId, string expectedError)
    {
        var req = new OAuthConfigRequest { Authority = authority, ClientId = clientId };
        var act = () => OAuthConfigService.Validate(req);
        act.Should().Throw<ArgumentException>().WithMessage("*" + expectedError + "*");
    }

    [Fact]
    public void Validate_RejectsHttpAuthority_WithoutAllowInsecure()
    {
        var req = ValidRequest();
        req.Authority = "http://localhost:8084";
        var act = () => OAuthConfigService.Validate(req);
        act.Should().Throw<ArgumentException>().WithMessage("*https*");
    }

    [Fact]
    public void Validate_AcceptsHttpAuthority_WithAllowInsecure()
    {
        var req = ValidRequest();
        req.Authority = "http://localhost:8084";
        req.AllowInsecure = true;
        var act = () => OAuthConfigService.Validate(req);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsInvalidScope()
    {
        var req = ValidRequest();
        req.Scopes = ["openid", "bad scope!"];
        var act = () => OAuthConfigService.Validate(req);
        act.Should().Throw<ArgumentException>().WithMessage("*invalid scope*");
    }

    [Fact]
    public void NormalizeScopes_AlwaysIncludesOpenIdProfileEmail_AndDedups()
    {
        OAuthConfigService.NormalizeScopes(null)
            .Should().BeEquivalentTo("openid", "profile", "email");
        OAuthConfigService.NormalizeScopes(["openid", "profile", "email"])
            .Should().BeEquivalentTo("openid", "profile", "email");
        OAuthConfigService.NormalizeScopes(["openid", "offline_access", "email"])
            .Should().BeEquivalentTo("openid", "profile", "email", "offline_access");
    }

    [Fact]
    public void ScopeSplit_SplitsOnSpaceAndComma()
    {
        OAuthConfigService.ScopeSplit("openid profile,email")
            .Should().BeEquivalentTo("openid", "profile", "email");
        OAuthConfigService.ScopeSplit("   ").Should().BeEmpty();
        OAuthConfigService.ScopeSplit("").Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigureAsync_WritesConfig_AndTakesEffectWithoutRestart()
    {
        var (svc, path) = Create(issuer: null);
        try
        {
            var result = await svc.ConfigureAsync(ValidRequest(), TestContext.Current.CancellationToken);

            result.RestartRequired.Should().BeFalse();
            result.Authority.Should().Be("https://id.example.com/realms/fdroid");
            result.ClientId.Should().Be("fdroidmgr");
            result.Scopes.Should().BeEquivalentTo("openid", "profile", "email");

            var json = Read(path);
            json.Should().Contain("\"OAuthIssuer\": \"https://id.example.com/realms/fdroid\"");
            json.Should().Contain("\"OAuthClientId\": \"fdroidmgr\"");
            json.Should().NotContain("OAuthClientSecret");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ConfigureAsync_MergesExistingUnrelatedKeys()
    {
        var (svc, path) = Create(issuer: null);
        try
        {
            File.WriteAllText(path, "{\"Logging\":{\"LogLevel\":{\"Default\":\"Information\"}}}");
            await svc.ConfigureAsync(ValidRequest(), TestContext.Current.CancellationToken);

            var json = Read(path);
            json.Should().Contain("\"Logging\"");
            json.Should().Contain("\"OAuthIssuer\"");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ConfigureAsync_SecondCall_ThrowsAlreadyConfigured()
    {
        var (svc, path) = Create(issuer: null);
        try
        {
            await svc.ConfigureAsync(ValidRequest(), TestContext.Current.CancellationToken);
            var act = async () => await svc.ConfigureAsync(ValidRequest(), TestContext.Current.CancellationToken);
            await act.Should().ThrowAsync<OAuthAlreadyConfiguredException>()
                .WithMessage("*already configured*");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ConfigureAsync_ConfiguredAtStartup_ThrowsAlreadyConfigured()
    {
        var (svc, path) = Create(issuer: "https://id.example.com/realms/fdroid");
        try
        {
            var act = async () => await svc.ConfigureAsync(ValidRequest(), TestContext.Current.CancellationToken);
            (await act.Should().ThrowAsync<OAuthAlreadyConfiguredException>())
                .Which.Authority.Should().Be("https://id.example.com/realms/fdroid");
        }
        finally { Cleanup(path); }
    }

    private static (OAuthConfigService Svc, string ConfigPath) Create(string? issuer = null, string? masterKey = null)
    {
        var configPath = Path.Combine(Path.GetTempPath(), "fdroidmgr-oauth-" + Guid.NewGuid().ToString("N") + ".json");
        var dict = new Dictionary<string, string?>
        {
            ["Auth:OAuthIssuer"] = issuer,
            ["FDROID_CONFIG_FILE"] = configPath,
            ["FDROID_MASTER_KEY"] = masterKey ?? MasterKey,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var svc = new OAuthConfigService(config, NullLogger<OAuthConfigService>.Instance, TestIo.Real);
        return (svc, configPath);
    }

    private static OAuthConfigRequest ValidRequest() => new()
    {
        Authority = "https://id.example.com/realms/fdroid",
        ClientId = "fdroidmgr",
        Scopes = ["openid", "email"],
    };

    private static string Read(string path) => File.ReadAllText(path);

    private static void Cleanup(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } } catch (IOException) { }
    }
}
