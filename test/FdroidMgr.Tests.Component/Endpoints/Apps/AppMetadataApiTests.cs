using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Apps;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class AppMetadataApiTests
{
    [Fact]
    public async Task GetMetadata_RequiresAuth()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.GetAsync("/api/apps/org.example.meta.x/metadata");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PostMetadata_RequiresAuth()
    {
        var (repoRoot, _, factory, client) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await client.PostAsync("/api/apps/org.example.meta.x/metadata", null);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task CreateApp_WritesMetadataFile()
    {
        var (repoRoot, _, factory, _) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            await CreateAppAsync(c, "org.example.meta.create");

            var filePath = Path.Combine(repoRoot, "metadata", "org.example.meta.create.yml");
            File.Exists(filePath).Should().BeTrue();
            var content = await File.ReadAllTextAsync(filePath);
            content.Should().Contain("Name: Meta Test App");
            content.Should().Contain("Summary: summary");
            content.Should().Contain("License: MIT");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task GetMetadata_UnknownApp_Returns404()
    {
        var (repoRoot, _, factory, _) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var res = await c.GetAsync("/api/apps/org.example.meta.unknown/metadata");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PostMetadata_UnknownApp_Returns404()
    {
        var (repoRoot, _, factory, _) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var res = await c.PostAsync("/api/apps/org.example.meta.unknown/metadata", null);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PostMetadata_WritesFile_ReturnsInfo_AndGetReturnsYaml()
    {
        var (repoRoot, runner, factory, _) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.meta.hello";
            await CreateAppAsync(c, pkg);

            var updatesBefore = runner.Log.Count(i => i.Exe == "fdroid" && i.Args.Contains("update"));

            var res = await c.PostAsync($"/api/apps/{pkg}/metadata", null);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("packageName").GetString().Should().Be(pkg);
            body.GetProperty("path").GetString().Should().Be(Path.Combine(repoRoot, "metadata", pkg + ".yml"));
            body.GetProperty("indexRegenerated").GetBoolean().Should().BeTrue();

            runner.Log.Count(i => i.Exe == "fdroid" && i.Args.Contains("update")).Should().Be(updatesBefore + 1);
            var inv = runner.Log.Last(i => i.Exe == "fdroid" && i.Args.Contains("update"));
            inv.Args.Should().Contain("--rename-apks");
            inv.WorkDir.Should().Be(repoRoot);

            var filePath = Path.Combine(repoRoot, "metadata", pkg + ".yml");
            var content = await File.ReadAllTextAsync(filePath);
            var get = await c.GetAsync($"/api/apps/{pkg}/metadata");
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            var getBody = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement;
            getBody.GetProperty("yaml").GetString().Should().Be(content);
            getBody.GetProperty("path").GetString().Should().Be(filePath);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PostMetadata_IsIdempotent()
    {
        var (repoRoot, _, factory, _) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.meta.idem";
            await CreateAppAsync(c, pkg);

            var first = await c.PostAsync($"/api/apps/{pkg}/metadata", null);
            first.StatusCode.Should().Be(HttpStatusCode.OK);

            var filePath = Path.Combine(repoRoot, "metadata", pkg + ".yml");
            var before = await File.ReadAllTextAsync(filePath);

            var second = await c.PostAsync($"/api/apps/{pkg}/metadata", null);
            second.StatusCode.Should().Be(HttpStatusCode.OK);

            (await File.ReadAllTextAsync(filePath)).Should().Be(before);
            before.Should().StartWith("Name: Meta Test App");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PostMetadata_FdroidFails_EditSucceeds_AndLastErrorRecorded()
    {
        var (repoRoot, _, factory, _) = await BootAsync(r => r.SimulateUpdateFailure = true);
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.meta.fail";
            await CreateAppAsync(c, pkg);

            var res = await c.PostAsync($"/api/apps/{pkg}/metadata", null);
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            File.Exists(Path.Combine(repoRoot, "metadata", pkg + ".yml")).Should().BeTrue();

            var statusRes = await c.GetAsync("/api/repo/status");
            statusRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var status = JsonDocument.Parse(await statusRes.Content.ReadAsStringAsync()).RootElement;
            status.GetProperty("lastIndexError").GetString().Should().Contain("fdroid update failed");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task UpdateApp_EditsMetadataFields()
    {
        var (repoRoot, _, factory, _) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.meta.update";
            await CreateAppAsync(c, pkg);

            var res = await c.PutAsync($"/api/apps/{pkg}", Json(new { name = "Renamed", license = "Apache-2.0" }));
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await File.ReadAllTextAsync(Path.Combine(repoRoot, "metadata", pkg + ".yml"));
            content.Should().Contain("Name: Renamed");
            content.Should().Contain("License: Apache-2.0");
            content.Should().Contain("Summary: summary");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static Task<(string repoRoot, FakeCommandRunner runner, WebApplicationFactory<Program> factory, HttpClient client)>
        BootAsync(Action<FakeCommandRunner>? configure = null)
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-meta.db",
            DirPrefix = "fdroidmgr-api-meta",
            Runner = configure,
        });
        return Task.FromResult((host.Root, host.Runner, host.Factory, host.Client()));
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return c;
    }

    private async Task CreateAppAsync(HttpClient client, string package)
    {
        var res = await client.PostAsync("/api/apps", Json(new
        {
            packageName = package,
            name = "Meta Test App",
            summary = "summary",
            license = "MIT",
            generateKey = false,
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
            .GetProperty("packageName").GetString().Should().Be(package);
    }
}
