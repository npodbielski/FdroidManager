using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class IndexFreshnessApiTests
{
    private const string Pkg = "org.example.fresh";

    [Fact]
    public async Task WriteMetadata_TriggerRebuild_FreshTimestampAndDescriptionInIndex()
    {
        using var host = Boot("index-fresh-meta.db", "fdroidmgr-index-fresh-meta");

        SeedMetadata(host, "Fresh App", "fresh summary", "the new description");
        host.Start();

        var c = Auth(host.Factory);
        var baseline = (await StatusAsync(c)).GetProperty("lastIndexBuiltUtc");

        var res = await c.PostAsync($"/api/apps/{Pkg}/metadata", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var s = await StatusAsync(c);
        var built = s.GetProperty("lastIndexBuiltUtc");

        if (baseline.ValueKind == JsonValueKind.Null)
        {
            built.ValueKind.Should().Be(JsonValueKind.String);
        }
        else
        {

            var before = DateTime.Parse(baseline.GetString()!).ToUniversalTime();
            while (DateTime.UtcNow <= before.AddMilliseconds(250))
            {
                await Task.Delay(50);
            }

            built.ValueKind.Should().Be(JsonValueKind.String);
            DateTime.Parse(built.GetString()!).ToUniversalTime().Should().BeAfter(before);
        }
        s.GetProperty("lastIndexError").ValueKind.Should().Be(JsonValueKind.Null);
        (await OnDiskIndexAsync(host)).Should().Contain("the new description");
    }

    [Fact]
    public async Task Update_TriggerRebuild_NewNameInIndex()
    {
        using var host = Boot("index-fresh-update.db", "fdroidmgr-index-fresh-update");
        SeedMetadata(host, "Old Name", "old summary", "old description");
        host.Start();

        var c = Auth(host.Factory);
        var res = await c.PutAsync($"/api/apps/{Pkg}", Json(new { name = "Brand New Name" }));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        (await OnDiskIndexAsync(host)).Should().Contain("Brand New Name").And.NotContain("Old Name");
    }

    [Fact]
    public async Task UploadIcon_TriggerRebuild_NewIconUrlInIndex()
    {
        using var host = Boot("index-fresh-icon.db", "fdroidmgr-index-fresh-icon");
        SeedMetadata(host, "Icon App", "has an icon", "icon description");
        host.Start();

        var c = Auth(host.Factory);
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), "file", "icon.png" },
        };
        var res = await c.PostAsync($"/api/apps/{Pkg}/icon", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        (await OnDiskIndexAsync(host)).Should().Contain($"{Pkg}/en-US/icon.png");
    }

    [Fact]
    public async Task FailedRebuild_EditStillSucceeds_StatusShowsLastError()
    {

        using var host = new TestHost(new TestHostOptions
        {
            DbName = "index-fresh-fail.db",
            DirPrefix = "fdroidmgr-index-fresh-fail",
            Runner = r => r.SimulateUpdateFailure = true,
        });
        SeedMetadata(host, "Fail App", "s", "d");
        host.Start();

        var c = Auth(host.Factory);
        var res = await c.PutAsync($"/api/apps/{Pkg}", Json(new { name = "Renamed Anyway" }));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var s = await StatusAsync(c);
        s.GetProperty("lastIndexError").GetString().Should().Be("mock: fdroid update failed");
        s.GetProperty("lastIndexBuiltUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static TestHost Boot(string db, string dirPrefix)
    {
        return new TestHost(new TestHostOptions
        {
            DbName = db,
            DirPrefix = dirPrefix,
            Runner = r => r.Handler = (exe, args, workDir, _) =>
                exe == "fdroid" && args.StartsWith("update", StringComparison.Ordinal)
                    ? Ingest(workDir)
                    : new CommandResult(0, "", ""),
        });
    }

    private static CommandResult Ingest(string repoRoot)
    {
        Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
        var metadataDir = Path.Combine(repoRoot, "metadata");
        var apps = new List<object>();
        if (Directory.Exists(metadataDir))
        {
            foreach (var file in Directory.EnumerateFiles(metadataDir, "*.yml"))
            {
                var packageName = Path.GetFileNameWithoutExtension(file);
                var f = FdroidMetadataService.ParseAppMetadata(File.ReadAllText(file));
                var icon = File.Exists(Path.Combine(metadataDir, packageName, "en-US", "icon.png"))
                    ? $"{packageName}/en-US/icon.png"
                    : null;
                apps.Add(new
                {
                    packageName,
                    name = f.Name,
                    summary = f.Summary,
                    description = f.Description,
                    license = f.License,
                    icon,
                });
            }
        }
        File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v1.json"), JsonSerializer.Serialize(new
        {
            repo = new { name = "Test Repo", address = "http://localhost/fdroid/repo", timestamp = 1 },
            apps,
            packages = new Dictionary<string, object[]>(),
        }));
        return new CommandResult(0, "update ok", "");
    }

    private static void SeedMetadata(TestHost host, string name, string summary, string description)
    {
        var dir = Path.Combine(host.Root, "metadata");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Pkg + ".yml"), FdroidMetadataService.Render(new AppMetadataFields
        {
            Name = name,
            Summary = summary,
            Description = description,
            License = "MIT",
        }, null));
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

    private static Task<string> OnDiskIndexAsync(TestHost host)
        => File.ReadAllTextAsync(FdroidRepoService.IndexPath(host.Root));

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
}
