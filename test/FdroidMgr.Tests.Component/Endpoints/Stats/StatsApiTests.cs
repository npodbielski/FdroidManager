using System.Net;
using System.Text.Json;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Stats;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class StatsApiTests
{
    private const string MasterKey = "stats-master-key-0123456789abcdef";

    [Fact]
    public async Task Stats_Interactive_Returns200_WithShape()
    {
        var factory = NewInteractiveFactory();
        try
        {
            var res = await Auth(factory).GetAsync("/api/stats/downloads");
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("totalDownloads").GetInt64().Should().Be(0);
            body.GetProperty("apps").GetArrayLength().Should().Be(0);
            body.GetProperty("releases").GetArrayLength().Should().Be(0);
            body.GetProperty("daily").GetArrayLength().Should().Be(30);
        }
        finally { Dispose(factory); }
    }

    [Fact]
    public async Task Stats_Unauthenticated_Returns401()
    {
        var factory = NewInteractiveFactory();
        try
        {
            var res = await factory.CreateClient().GetAsync("/api/stats/downloads");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { Dispose(factory); }
    }

    [Fact]
    public async Task Stats_ApiKey_Returns403_InteractiveOnly()
    {
        var (factory, root) = NewApiKeyFactory();
        try
        {
            var key = await SeedKeyAsync(factory);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);

            var res = await client.GetAsync("/api/stats/downloads");
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { Dispose(factory, root); }
    }

    [Fact]
    public async Task ApkDownload_IncrementsCounters()
    {
        var factory = NewInteractiveFactory();
        try
        {
            var client = Auth(factory);

            (await client.GetAsync("/fdroid/repo/com.example_3.apk")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/fdroid/repo/com.example_3.apk")).StatusCode.Should().Be(HttpStatusCode.OK);

            // The writer drains the queue outside the request, so the counters
            // become visible a moment after the download completes.
            var body = await WaitUntilAsync(client, 2);

            body.GetProperty("totalDownloads").GetInt64().Should().Be(2);
            var apps = body.GetProperty("apps");
            apps.GetArrayLength().Should().Be(1);
            apps[0].GetProperty("packageName").GetString().Should().Be("com.example");
            apps[0].GetProperty("downloads").GetInt64().Should().Be(2);

            var releases = body.GetProperty("releases");
            releases.GetArrayLength().Should().Be(1);
            releases[0].GetProperty("versionCode").GetInt32().Should().Be(3);
            releases[0].GetProperty("downloads").GetInt64().Should().Be(2);
        }
        finally { Dispose(factory); }
    }

    private static async Task<JsonElement> WaitUntilAsync(HttpClient client, long expected)
    {
        for (var i = 0; i < 200; i++)
        {
            var res = await client.GetAsync("/api/stats/downloads");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            if (body.GetProperty("totalDownloads").GetInt64() == expected)
            {
                return body;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"download counters never reached {expected}");
    }

    private static void Dispose(WebApplicationFactory<Program> factory, string? root = null)
    {
        factory.Dispose();
        if (root is null)
        {
            return;
        }

        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {

        }
    }

    private static WebApplicationFactory<Program> NewInteractiveFactory()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "stats.db",
            MasterKey = MasterKey,
            DirPrefix = "fdroidmgr-statsapi",
        });

        File.WriteAllBytes(Path.Combine(host.RepoDir, "com.example_3.apk"), [0x50, 0x4B, 0x03, 0x04]);

        return host.Factory;
    }

    private static (WebApplicationFactory<Program> Factory, string Root) NewApiKeyFactory()
    {

        var host = new TestHost(new TestHostOptions
        {
            DbName = "statskey.db",
            MasterKey = MasterKey,
            TestAuth = false,
            DirPrefix = "fdroidmgr-statskey",
        });
        return (host.Factory, host.Root);
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return c;
    }

    private static async Task<string> SeedKeyAsync(WebApplicationFactory<Program> factory)
    {
        var plaintext = new ApiKeyService(null!).GenerateSecret();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();

        var user = new ApiUser { Subject = "stats-sub", Username = "alice", CreatedUtc = DateTime.UtcNow };
        db.ApiUsers.Add(user);
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            UserId = user.Id,
            Name = "ci",
            KeyHash = new ApiKeyService(null!).Hash(plaintext),
            KeyPrefix = new ApiKeyService(null!).Prefix(plaintext),
            Scopes = "read",
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return plaintext;
    }
}
