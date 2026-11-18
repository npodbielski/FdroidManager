using System.Net;
using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

public class RepoAuthTests
{
    private const string MasterKey = "repo-auth-master-key-0123456789abcdef";

    [Fact]
    public async Task GatingOn_BasicAuth_ValidKey_ServesIndex()
    {
        var (factory, root) = NewFactory(requireRepoAuth: true);
        try
        {
            var key = await SeedKeyAsync(factory);
            var client = NewClient(factory);
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Basic("testuser", key));

            var res = await client.GetAsync("/fdroid/repo/index.xml");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            (await res.Content.ReadAsStringAsync()).Should().Contain("<fdroid");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task GatingOn_BasicAuth_InvalidKey_Returns401Json()
    {
        var (factory, root) = NewFactory(requireRepoAuth: true);
        try
        {
            var client = NewClient(factory);
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Basic("testuser", "fdmgr_wrong"));

            var res = await client.GetAsync("/fdroid/repo/index.xml");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            res.Headers.Location.Should().BeNull();
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("unauthorized");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task GatingOn_QueryKey_Valid_ServesIndex()
    {
        var (factory, root) = NewFactory(requireRepoAuth: true);
        try
        {
            var key = await SeedKeyAsync(factory);
            var client = NewClient(factory);

            var res = await client.GetAsync("/fdroid/repo/index.xml?key=" + Uri.EscapeDataString(key));
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task GatingOn_Anonymous_Returns401Json_NotRedirect()
    {
        var (factory, root) = NewFactory(requireRepoAuth: true);
        try
        {
            var client = NewClient(factory);
            var res = await client.GetAsync("/fdroid/repo/index.xml");

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            res.Headers.Location.Should().BeNull();
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("unauthorized");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task GatingOff_Anonymous_ServesIndex()
    {
        var (factory, root) = NewFactory(requireRepoAuth: false);
        try
        {
            var client = NewClient(factory);
            var res = await client.GetAsync("/fdroid/repo/index.xml");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task NothingConfigured_IsGated()
    {
        // An unset FDROID_REQUIRE_REPO_AUTH must not mean "open": a deployment
        // that never heard of the flag must not publish its repository by accident.
        var host = new TestHost(new TestHostOptions
        {
            DbName = "repodefault.db",
            MasterKey = MasterKey,
            TestAuth = false,
            DirPrefix = "fdroidmgr-repodefault",
        });
        try
        {
            File.WriteAllText(Path.Combine(host.RepoDir, "index.xml"), "<fdroid><repo name=\"X\" /></fdroid>");
            var res = await host.Client(allowAutoRedirect: false).GetAsync("/fdroid/repo/index.xml");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { host.Dispose(); }
    }

    private static (WebApplicationFactory<Program> Factory, string Root) NewFactory(
        bool requireRepoAuth)
    {

        var host = new TestHost(new TestHostOptions
        {
            DbName = "repoauth.db",
            MasterKey = MasterKey,
            TestAuth = false,
            DirPrefix = "fdroidmgr-repoauth",
            Settings = new Dictionary<string, string?>
            {
                ["FDROID_REQUIRE_REPO_AUTH"] = requireRepoAuth ? "true" : "false",
            },
        });

        File.WriteAllText(Path.Combine(host.RepoDir, "index.xml"), "<fdroid><repo name=\"Gated\" /></fdroid>");

        return (host.Factory, host.Root);
    }

    private static async Task<string> SeedKeyAsync(WebApplicationFactory<Program> factory)
    {
        var plaintext = new ApiKeyService(null!).GenerateSecret();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();

        var user = new ApiUser { Subject = "repo-sub", Username = "alice", CreatedUtc = DateTime.UtcNow };
        db.ApiUsers.Add(user);
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            UserId = user.Id,
            Name = "f-droid-client",
            KeyHash = new ApiKeyService(null!).Hash(plaintext),
            KeyPrefix = new ApiKeyService(null!).Prefix(plaintext),
            Scopes = "read",
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return plaintext;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string Basic(string user, string key)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + key));

    private static void Dispose(WebApplicationFactory<Program> factory, string root)
    {
        factory.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}
