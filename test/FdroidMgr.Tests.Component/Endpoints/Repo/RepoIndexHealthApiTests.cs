using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class RepoIndexHealthApiTests
{
    [Fact]
    public async Task Status_IndexIsHealthy_AfterSuccessfulRebuild()
    {
        var (repoRoot, _, factory) = await BootAsync();
        await using var closer = factory;
        try
        {
            var c = Auth(factory);

            var rebuild = await c.PostAsync("/api/repo/index/rebuild", null);
            rebuild.StatusCode.Should().Be(HttpStatusCode.OK);

            var s = await StatusAsync(c);
            s.GetProperty("indexHealthy").GetBoolean().Should().BeTrue();
            s.GetProperty("lastIndexError").ValueKind.Should().Be(JsonValueKind.Null);
            s.GetProperty("lastIndexBuiltUtc").ValueKind.Should().Be(JsonValueKind.String);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Status_IndexIsUnhealthy_AfterFailedRebuild()
    {
        var (repoRoot, _, factory) = await BootAsync(simulateFailure: true);
        await using var closer = factory;
        try
        {
            var c = Auth(factory);

            var rebuild = await c.PostAsync("/api/repo/index/rebuild", null);
            rebuild.StatusCode.Should().Be(HttpStatusCode.OK);

            var s = await StatusAsync(c);
            s.GetProperty("indexHealthy").GetBoolean().Should().BeFalse();
            s.GetProperty("lastIndexError").GetString().Should().Be("mock: fdroid update failed");
            s.GetProperty("lastIndexBuiltUtc").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Status_IsPublic_NoAuthRequired()
    {
        var (repoRoot, _, factory) = await BootAsync();
        await using var closer = factory;
        try
        {

            var s = await StatusAsync(factory.CreateClient());
            s.TryGetProperty("repoName", out _).Should().BeTrue();
            s.TryGetProperty("indexHealthy", out _).Should().BeTrue();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    private static Task<(string repoRoot, FakeCommandRunner runner, WebApplicationFactory<Program> factory)>
        BootAsync(bool simulateFailure = false)
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-health.db",
            DirPrefix = "fdroidmgr-api-health",
            Runner = runner => runner.SimulateUpdateFailure = simulateFailure,
        });
        host.Start();
        return Task.FromResult((host.Root, host.Runner, host.Factory));
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return c;
    }

    private static async Task<JsonElement> StatusAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/repo/status");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }
}
