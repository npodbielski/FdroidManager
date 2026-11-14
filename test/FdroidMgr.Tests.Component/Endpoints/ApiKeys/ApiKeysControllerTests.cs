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

namespace FdroidMgr.Tests.Component.Endpoints.ApiKeys;

public class ApiKeysControllerTests
{

    [Fact]
    public async Task Create_ReturnsSecretOnce_ListNeverReturnsSecret()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Auth(factory);

            var create = await client.PostAsync("/api/api-keys",
                Json(new { name = "CI pipeline", scopes = new[] { "read" } }));
            create.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await create.Content.ReadAsAsync<ApiKeyCreatedResponse>();
            var id = created.Id;
            var secret = created.Key;
            secret.Should().NotBeNullOrEmpty();
            secret.Should().StartWith("fdmgr_");

            created.RepoBasicAuthUrl.Should().Contain(":" + secret + "@");
            created.RepoBasicAuthUrl.Should().EndWith("/fdroid/repo");

            var list = await client.GetAsync("/api/api-keys");
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var raw = await list.Content.ReadAsStringAsync();
            raw.Should().NotContain(secret);

            var items = JsonDocument.Parse(raw).RootElement;
            items.GetArrayLength().Should().Be(1);
            var item = items[0];
            item.GetProperty("id").GetInt32().Should().Be(id);
            item.GetProperty("name").GetString().Should().Be("CI pipeline");
            item.GetProperty("keyPrefix").GetString().Should().Be(secret[..10]);
            item.TryGetProperty("key", out _).Should().BeFalse();
            item.GetProperty("scopes").EnumerateArray().Select(e => e.GetString())
                .Should().Contain(["read"]);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task Create_WriteScope_ImpliesRead_AndIsPersistedExpanded()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Auth(factory);
            var create = await client.PostAsync("/api/api-keys",
                Json(new { name = "release-bot", scopes = new[] { "releases:write" } }));
            create.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();
            var stored = await db.ApiKeys.AsNoTracking().SingleAsync();
            new ApiKeyService(null!).SplitScopes(stored.Scopes).Should().BeEquivalentTo("read", "releases:write");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task Create_InvalidScope_Returns400()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Auth(factory);
            var res = await client.PostAsync("/api/api-keys",
                Json(new { name = "bad", scopes = new[] { "admin" } }));
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("invalid scope");
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task Create_MissingScopes_Returns400()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Auth(factory);
            var res = await client.PostAsync("/api/api-keys", Json(new { name = "no-scopes" }));
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task Revoke_OwnKey_MarksRevoked()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Auth(factory);
            var create = await client.PostAsync("/api/api-keys",
                Json(new { name = "temp", scopes = new[] { "read" } }));
            var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

            var revoke = await client.DeleteAsync($"/api/api-keys/{id}");
            revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var list = await client.GetAsync("/api/api-keys");
            var item = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement[0];
            item.GetProperty("revokedUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task Revoke_UnknownId_Returns404()
    {
        var (factory, root) = NewFactory();
        try
        {
            var client = Auth(factory);
            var res = await client.DeleteAsync("/api/api-keys/99999");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { Dispose(factory, root); }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static (WebApplicationFactory<Program> Factory, string Root) NewFactory()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "keys.db",
            DirPrefix = "fdroidmgr-apikeys",
        });
        return (host.Factory, host.Root);
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return client;
    }

    private static void Dispose(WebApplicationFactory<Program> factory, string root)
    {
        factory.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}
