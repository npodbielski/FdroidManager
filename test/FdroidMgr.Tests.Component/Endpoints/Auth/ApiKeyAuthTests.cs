using System.Net;
using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Auth;

public class ApiKeyAuthTests
{
    private const string MasterKey = "apikey-auth-master-key-0123456789abcdef";

    [Fact]
    public async Task ApiKey_InvalidKey_Returns401Json_NotRedirect()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Client(factory);
            Auth(client, "fdmgr_bogus-key-that-was-never-issued");

            var res = await client.GetAsync("/api/apps");

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            res.Headers.Location.Should().BeNull();
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Be("unauthorized");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_EmptyBearer_Returns401Json_NotRedirect()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Client(factory);
            Auth(client, "");

            var res = await client.GetAsync("/api/apps");

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            res.Headers.Location.Should().BeNull();
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("unauthorized");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_NoHeader_StillReturns401Json_NotRedirect()
    {

        var (factory, root) = NewFactory();
        try
        {
            var client = Client(factory);
            var res = await client.GetAsync("/api/apps");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            res.Headers.Location.Should().BeNull();
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("unauthorized");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_ValidReadScope_AllowsGetApps()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read");
            var client = Client(factory);
            Auth(client, seeded);

            var res = await client.GetAsync("/api/apps");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_XApiKeyHeader_Works()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read");
            var client = Client(factory);
            client.DefaultRequestHeaders.Add("X-API-Key", seeded);

            var res = await client.GetAsync("/api/apps");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_Revoked_Returns401()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read", revokedUtc: DateTime.UtcNow);
            var client = Client(factory);
            Auth(client, seeded);

            var res = await client.GetAsync("/api/apps");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_Expired_Returns401()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read", expiresUtc: DateTime.UtcNow.AddMinutes(-5));
            var client = Client(factory);
            Auth(client, seeded);

            var res = await client.GetAsync("/api/apps");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_ReadScope_CannotWriteApp()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read");
            var client = Client(factory);
            Auth(client, seeded);

            var res = await client.PostAsync("/api/apps", Json(new { packageName = "org.example.app", name = "App" }));
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            res.Headers.Location.Should().BeNull();
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("forbidden");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_ReleasesWrite_ImpliesRead_ButNotAppsWrite()
    {
        var (factory, root) = NewFactory();
        try
        {

            var seeded = await SeedKeyAsync(factory, "releases:write");
            var client = Client(factory);
            Auth(client, seeded);

            (await client.GetAsync("/api/apps")).StatusCode.Should().Be(HttpStatusCode.OK);

            var post = await client.PostAsync("/api/apps", Json(new { packageName = "org.example.app", name = "App" }));
            post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_AppsWrite_CanGetApps_ButNotPublishRelease()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "apps:write");
            var client = Client(factory);
            Auth(client, seeded);

            (await client.GetAsync("/api/apps")).StatusCode.Should().Be(HttpStatusCode.OK);

            var publish = await client.PostAsync("/api/apps/org.example.app/releases", new StringContent(""));
            publish.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_AdminRepoEndpoint_IsInteractiveOnly_RejectsApiKey()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read");
            var client = Client(factory);
            Auth(client, seeded);

            var res = await client.GetAsync("/api/repo");
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApiKey_StoredAsHashOnly()
    {
        var (factory, root) = NewFactory();
        try
        {
            var seeded = await SeedKeyAsync(factory, "read");
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();

            var stored = await db.ApiKeys.AsNoTracking().SingleAsync();
            stored.KeyHash.Should().Be(new ApiKeyService(null!).Hash(seeded));
            stored.KeyHash.Should().NotBe(seeded);
            stored.KeyPrefix.Should().Be(seeded[..10]);
            stored.KeyPrefix.Should().NotBe(seeded);

            new[] { stored.Name, stored.KeyHash, stored.KeyPrefix, stored.Scopes }
                .Should().NotContain(seeded);
        }
        finally { Dispose(factory, root); }
    }

    private static (WebApplicationFactory<Program> Factory, string Root) NewFactory()
    {

        var host = new TestHost(new TestHostOptions
        {
            DbName = "apikey.db",
            MasterKey = MasterKey,
            TestAuth = false,
            DirPrefix = "fdroidmgr-apikey",
        });
        return (host.Factory, host.Root);
    }

    private static async Task<string> SeedKeyAsync(WebApplicationFactory<Program> factory,
        string scopes, DateTime? expiresUtc = null, DateTime? revokedUtc = null)
    {
        var plaintext = new ApiKeyService(null!).GenerateSecret();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();

        var user = new ApiUser { Subject = "sub-1", Username = "alice", CreatedUtc = DateTime.UtcNow };
        db.ApiUsers.Add(user);
        await db.SaveChangesAsync();

        var key = new ApiKey
        {
            UserId = user.Id,
            Name = "ci-pipeline",
            KeyHash = new ApiKeyService(null!).Hash(plaintext),
            KeyPrefix = new ApiKeyService(null!).Prefix(plaintext),
            Scopes = scopes,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = expiresUtc,
            RevokedUtc = revokedUtc,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();
        return plaintext;
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static void Auth(HttpClient client, string token)
        => client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static void Dispose(WebApplicationFactory<Program> factory, string root)
    {
        factory.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}
