using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

public class RepoInitApiTests
{
    private const string MasterKey = TestHost.DefaultMasterKey;

    [Fact]
    public async Task InitStatus_Public_ReportsUninitialized()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.GetAsync("/api/repo/init/status");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("initialized").GetBoolean().Should().BeFalse();
            body.GetProperty("repoPath").GetString().Should().NotBeNullOrEmpty();
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_RequiresAuth()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.PostAsync("/api/repo/init", Json(new { repoName = "X" }));
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_DerivesPublicUrl_Returns201()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = await AuthAsync(factory, client);
            var res = await c.PostAsync("/api/repo/init", Json(new { repoName = "Derived" }));
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            var url = body.GetProperty("repoUrl").GetString();
            url.Should().NotBeNullOrEmpty();
            url.Should().MatchRegex("^https?://");
            url.Should().EndWith("/repo");
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_MissingRepoName_Returns400()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = await AuthAsync(factory, client);
            var res = await c.PostAsync("/api/repo/init", Json(new { }));
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("repoName");
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_BlankKeyAlias_Returns400()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = await AuthAsync(factory, client);
            var res = await c.PostAsync("/api/repo/init", Json(new
            {
                repoName = "X",
                keyAlias = " ",
            }));
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_FreshRepo_Returns201_AndCreatesFiles()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = await AuthAsync(factory, client);
            var res = await c.PostAsync("/api/repo/init", Json(new
            {
                repoName = "API-Init Repo",
                repoDescription = "created via API",
                keyAlias = "api-init",
            }));
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("repoUrl").GetString().Should().Be("http://localhost:5000/fdroid/repo");
            body.GetProperty("repoName").GetString().Should().Be("API-Init Repo");
            body.GetProperty("repoPath").GetString().Should().Be(Path.Combine(repoRoot, "repo"));
            body.GetProperty("keyAlias").GetString().Should().Be("api-init");

            File.Exists(Path.Combine(repoRoot, "config.yml")).Should().BeTrue();
            File.Exists(Path.Combine(repoRoot, "keystore.p12")).Should().BeTrue();
            File.Exists(Path.Combine(repoRoot, "repo", "index.xml")).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_SecondCall_WithoutForce_Returns409()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = await AuthAsync(factory, client);
            var req = new { repoName = "X" };

            var first = await c.PostAsync("/api/repo/init", Json(req));
            first.StatusCode.Should().Be(HttpStatusCode.Created);

            var second = await c.PostAsync("/api/repo/init", Json(req));
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("already initialized");
            FieldEl(body, "backupPath").Should().NotBeNull();
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Init_FdroidFails_Returns502()
    {
        var (repoRoot, _, factory, client) = await BootAsync(r => r.SimulateInitFailure = true);
        await using var _ = factory;
        try
        {
            var c = await AuthAsync(factory, client);
            var res = await c.PostAsync("/api/repo/init", Json(new
            {
                repoName = "X",
            }));
            res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("fdroid init failed");
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static string? FieldEl(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v.ToString() : null;

    private static Task<(string repoRoot, FakeCommandRunner runner, WebApplicationFactory<Program> factory, HttpClient
            client)>
        BootAsync(Action<FakeCommandRunner>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "fdroidmgr-api-init-" + Guid.NewGuid().ToString("N"));
        var host = new TestHost(new TestHostOptions
        {
            Root = root,
            DbName = "api-init.db",
            MasterKey = MasterKey,
            DirPrefix = "fdroidmgr-api-init",
            Runner = configure,
            Services = services =>
            {
                services.RemoveAll<IRepoInitService>();
                services.RemoveAll<RepoInitService>();
                services.AddScoped<IRepoInitService>(sp => new RepoInitService(
                    sp.GetRequiredService<Api.Data.FdroidDb>(),
                    sp.GetRequiredService<IRepoCommandRunner>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RepoInitService>>(),
                    new RealIoService(),
                    sp.GetRequiredService<IEnvironmentService>(),
                    sp.GetRequiredService<IPasswordProtector>())
                {
                    RepoRoot = root,
                    AndroidHome = "/opt/android-sdk",
                    MasterKey = MasterKey,
                });
            },
        });
        return Task.FromResult((host.Root, host.Runner, host.Factory, host.Client()));
    }

    private static Task<HttpClient> AuthAsync(WebApplicationFactory<Program> factory, HttpClient client)
    {
        _ = client;
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return Task.FromResult(c);
    }
}
