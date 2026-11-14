using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Apps;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class AppMediaApiTests
{
    private const string Pkg = "org.example.media";

    private const string Icon = "org.example.media.1.png";

    [Fact]
    public async Task ListAndDetail_ExposeIconAndScreenshots()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);

            var list = await c.GetAsync("/api/apps");
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var listEntry = JsonDocument.Parse(await list.Content.ReadAsStringAsync())
                .RootElement.EnumerateArray().Should().ContainSingle().Which;
            listEntry.GetProperty("packageName").GetString().Should().Be(Pkg);
            listEntry.GetProperty("icon").GetString().Should().Be("icons/" + Icon);

            var detail = await c.GetAsync($"/api/apps/{Pkg}");
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()).RootElement;
            detailJson.GetProperty("icon").GetString().Should().Be("icons/" + Icon);
            detailJson.GetProperty("screenshots").EnumerateArray()
                .Select(e => e.GetString()).Should().Equal($"{Pkg}/en-US/phoneScreenshots/home.png");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task List_PrefersV2IconOverV1()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {

            var v2 = JsonSerializer.Serialize(new
            {
                repo = new { name = "Test Repo", address = "http://localhost/fdroid/repo", timestamp = 1787760032000 },
                packages = new Dictionary<string, object>
                {
                    [Pkg] = new
                    {
                        metadata = new
                        {
                            icon = new Dictionary<string, object>
                            {
                                ["en-US"] = new { name = $"/{Pkg}/en-US/icon_abc.png", sha256 = "x", size = 123 },
                            },
                        },
                    },
                },
            });
            File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v2.json"), v2);

            var c = Auth(factory);
            var list = await c.GetAsync("/api/apps");
            var entry = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement
                .EnumerateArray().Should().ContainSingle().Which;
            entry.GetProperty("icon").GetString().Should().Be($"{Pkg}/en-US/icon_abc.png");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task StaticRoute_ServesSeededIconAsPng()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {

            var res = await factory.CreateClient().GetAsync($"/fdroid/repo/icons/{Icon}");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            res.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

            var shot = await factory.CreateClient().GetAsync($"/fdroid/repo/{Pkg}/en-US/phoneScreenshots/home.png");
            shot.StatusCode.Should().Be(HttpStatusCode.OK);
            shot.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    private static Task<(string repoRoot, WebApplicationFactory<Program> factory)> BootAsync()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-media.db",
            DirPrefix = "fdroidmgr-api-media",
        });
        var repoRoot = host.Root;

        var repoDir = host.RepoDir;
        Directory.CreateDirectory(Path.Combine(repoDir, "icons"));
        var screenshotDir = Path.Combine(repoDir, Pkg, "en-US", "phoneScreenshots");
        Directory.CreateDirectory(screenshotDir);

        File.WriteAllBytes(Path.Combine(repoDir, "icons", Icon), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        File.WriteAllBytes(Path.Combine(screenshotDir, "home.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var index = JsonSerializer.Serialize(new
        {
            repo = new { name = "Test Repo", address = "http://localhost/fdroid/repo", timestamp = 1787760032000 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps = new object[]
            {
                new
                {
                    packageName = Pkg,
                    name = "Media App",
                    summary = "has artwork",
                    description = "artwork app",
                    license = "MIT",
                    icon = Icon,
                    localized = new Dictionary<string, object>
                    {
                        ["en-US"] = new Dictionary<string, object>
                        {
                            ["phoneScreenshots"] = new[] { "home.png" },
                        },
                    },
                },
            },
            packages = new Dictionary<string, object[]>(),
        });
        File.WriteAllText(Path.Combine(repoDir, "index-v1.json"), index);

        host.Start();
        return Task.FromResult((repoRoot, host.Factory));
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return c;
    }
}
