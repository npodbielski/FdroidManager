using System.Net;
using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.FdroidClientKey;

public class FdroidClientKeyApiTests
{
    private const string MasterKey = "fdroid-client-key-test-0123456789abcdef";

    [Fact]
    public async Task Get_CreatesSingletonWithMaskedUrl()
    {
        var host = StubHost("fdroidmgr-fdk-stub");
        try
        {
            var client = host.Client(signedInAs: "alice");
            var state = await (await client.GetAsync("/api/fdroid-client-key"))
                .Content.ReadAsAsync<ClientKeyStateResponse>();

            var prefix = state.KeyPrefix;
            prefix.StartsWith("fdmgr_", StringComparison.Ordinal).Should().BeTrue();

            var url = state.RepoBasicAuthUrl;
            url.Should().Contain(prefix + "…@");
            state.LastRevealedUtc.Should().BeNull();
            state.Id.Should().BeGreaterThan(0);

            var secret = (await (await client.GetAsync("/api/fdroid-client-key/secret"))
                .Content.ReadAsAsync<ClientKeySecretResponse>()).Key;
            url.Should().NotContain(secret);
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Secret_ReturnsPlaintextAndTracksLastRevealed()
    {
        var host = StubHost("fdroidmgr-fdk-reveal");
        try
        {
            var client = host.Client(signedInAs: "alice");
            await GetJsonAsync(client, "/api/fdroid-client-key");

            var secret = await (await client.GetAsync("/api/fdroid-client-key/secret"))
                .Content.ReadAsAsync<ClientKeySecretResponse>();
            var key = secret.Key;
            key.StartsWith("fdmgr_", StringComparison.Ordinal).Should().BeTrue();
            key.Length.Should().BeGreaterThan(30);
            secret.RepoBasicAuthUrl.Should().Contain(":" + key + "@");
            secret.RegeneratedUtc.Should().BeNull("a plain reveal does not regenerate the key");
            secret.KeyPrefix.Should().NotBeNullOrEmpty();
            secret.RepoUrl.Should().NotBeNullOrEmpty();

            var after = await (await client.GetAsync("/api/fdroid-client-key"))
                .Content.ReadAsAsync<ClientKeyStateResponse>();
            after.LastRevealedUtc.Should().NotBeNull();
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Regenerate_ReplacesTheSecretTheOldOneIsGone()
    {
        var host = StubHost("fdroidmgr-fdk-regen");
        try
        {
            var client = host.Client(signedInAs: "alice");
            await GetJsonAsync(client, "/api/fdroid-client-key");

            var first = (await GetJsonAsync(client, "/api/fdroid-client-key/secret"))
                .GetProperty("key").GetString()!;

            var res = await client.PostAsync("/api/fdroid-client-key/regenerate", null);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var root = await res.Content.ReadAsAsync<ClientKeySecretResponse>();
            var second = root.Key;
            second.Should().NotBe(first);
            root.RegeneratedUtc.Should().NotBeNull();

            var again = (await GetJsonAsync(client, "/api/fdroid-client-key/secret"))
                .GetProperty("key").GetString()!;
            again.Should().Be(second);
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Get_IsASingletonAcrossUsers()
    {
        var host = StubHost("fdroidmgr-fdk-singleton");
        try
        {
            var a = await GetJsonAsync(host.Client(signedInAs: "alice"), "/api/fdroid-client-key");
            var b = await GetJsonAsync(host.Client(signedInAs: "bob"), "/api/fdroid-client-key");

            a.GetProperty("id").GetInt32().Should().Be(b.GetProperty("id").GetInt32());
            a.GetProperty("keyPrefix").GetString().Should().Be(b.GetProperty("keyPrefix").GetString());
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task RepoAuth_ClientKey_ServesIndex_OldSecretDiesAfterRegenerate()
    {
        var (host, s1) = await SeedClientKeyAsync("fdroidmgr-fdk-repo");
        try
        {
            var client = NewClient(host);

            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Basic("testuser", s1));
            var ok = await client.GetAsync("/fdroid/repo/index.xml");
            ok.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();
            var used = await db.FdroidClientKey.AsNoTracking().FirstAsync();
            used.LastUsedUtc.Should().NotBeNull();

            var svc = scope.ServiceProvider.GetRequiredService<IFdroidClientKeyService>();
            var (_, s2) = await svc.RegenerateAsync();
            s2.Should().NotBe(s1);

            client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Basic("testuser", s1));
            var dead = await client.GetAsync("/fdroid/repo/index.xml");
            dead.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            JsonDocument.Parse(await dead.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("unauthorized");

            client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + Basic("testuser", s2));
            var alive = await client.GetAsync("/fdroid/repo/index.xml");
            alive.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task InteractiveOnly_AKeyAuthenticatedCaller_CannotReadOrRevealTheKey()
    {
        var (host, _) = await SeedClientKeyAsync("fdroidmgr-fdk-interactive");
        try
        {

            var plaintext = new ApiKeyService(null!).GenerateSecret();
            using var scope = host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();
            var user = await db.ApiUsers.FirstAsync(u => u.Subject == "fdk-sub");
            db.ApiKeys.Add(new ApiKey
            {
                UserId = user.Id,
                Name = "read-key",
                KeyHash = new ApiKeyService(null!).Hash(plaintext),
                KeyPrefix = new ApiKeyService(null!).Prefix(plaintext),
                Scopes = "read",
                CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var client = NewClient(host);
            client.DefaultRequestHeaders.Add("X-API-Key", plaintext);

            var secret = await client.GetAsync("/api/fdroid-client-key/secret");
            secret.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            JsonDocument.Parse(await secret.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("forbidden");

            var state = await client.GetAsync("/api/fdroid-client-key");
            state.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { host.Dispose(); }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var res = await client.GetAsync(path);
        res.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private static TestHost StubHost(string dir)
        => new(new TestHostOptions
        {
            DbName = "fdroidkey-stub.db",
            DirPrefix = dir,
        });

    private static async Task<(TestHost Host, string S1)> SeedClientKeyAsync(string dir)
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "fdroidkey-repo.db",
            MasterKey = MasterKey,
            TestAuth = false,
            DirPrefix = dir,
            Settings = new Dictionary<string, string?>
            {
                ["FDROID_REQUIRE_REPO_AUTH"] = "true",
            },
        });

        File.WriteAllText(Path.Combine(host.RepoDir, "index.xml"), "<fdroid><repo name=\"Fdk\" /></fdroid>");

        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();
        var user = new ApiUser { Subject = "fdk-sub", Username = "testuser", CreatedUtc = DateTime.UtcNow };
        db.ApiUsers.Add(user);
        await db.SaveChangesAsync();

        var svc = scope.ServiceProvider.GetRequiredService<IFdroidClientKeyService>();
        await svc.GetOrCreateAsync(user);
        var (_, s1) = await svc.RevealAsync();
        return (host, s1);
    }

    private static HttpClient NewClient(TestHost host)
        => host.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string Basic(string user, string key)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + key));
}
