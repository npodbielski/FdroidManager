using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class MetadataServedTests
{
    private record Meta(string Package, string Name, string Summary, string Description, string License);

    [Fact]
    public async Task UpdatingMetadata_ChangesTheIndexServedToFdroidClients()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.served";

            var create = await c.PostAsync("/api/apps", Json(new
            {
                packageName = pkg,
                name = "Served App",
                summary = "old summary",
                license = "MIT",
                generateKey = false,
            }));
            create.StatusCode.Should().Be(HttpStatusCode.Created);

            var upd = await c.PutAsync($"/api/apps/{pkg}", Json(new
            {
                name = "Renamed App",
                summary = "fresh summary",
                description = "fresh description",
            }));
            upd.StatusCode.Should().Be(HttpStatusCode.OK);

            var yml = await File.ReadAllTextAsync(Path.Combine(repoRoot, "metadata", pkg + ".yml"));
            yml.Should().Contain("Name: Renamed App");
            yml.Should().Contain("Summary: fresh summary");
            yml.Should().Contain("Description: fresh description");

            var idx = await c.GetAsync("/fdroid/repo/index-v1.json");
            idx.StatusCode.Should().Be(HttpStatusCode.OK);
            var entry = JsonDocument.Parse(await idx.Content.ReadAsStringAsync())
                .RootElement.GetProperty("apps").EnumerateArray().Should().ContainSingle().Which;
            entry.GetProperty("packageName").GetString().Should().Be(pkg);
            entry.GetProperty("name").GetString().Should().Be("Renamed App");
            entry.GetProperty("summary").GetString().Should().Be("fresh summary");
            entry.GetProperty("description").GetString().Should().Be("fresh description");

            var xmlRes = await c.GetAsync("/fdroid/repo/index.xml");
            xmlRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var xml = await xmlRes.Content.ReadAsStringAsync();
            xml.Should().Contain("<name>Renamed App</name>");
            xml.Should().Contain("fresh summary");
            xml.Should().Contain("fresh description");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static Task<(string repoRoot, WebApplicationFactory<Program> factory)> BootAsync()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "served.db",
            DirPrefix = "fdroidmgr-served",

            Runner = runner => runner.Handler = (exe, args, dir, _) =>
            {
                if (exe == "fdroid" && args.Contains("update"))
                {
                    RegenerateIndexFromMetadata(dir);
                    return new CommandResult(0, "update ok", "");
                }

                return new CommandResult(0, "", "");
            },
        });
        host.Start();
        return Task.FromResult((host.Root, host.Factory));
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return c;
    }

    private static void RegenerateIndexFromMetadata(string repoRoot)
    {
        var repoDir = Path.Combine(repoRoot, "repo");
        Directory.CreateDirectory(repoDir);

        var metaDir = Path.Combine(repoRoot, "metadata");
        var metas = Directory.Exists(metaDir)
            ? Directory.GetFiles(metaDir, "*.yml")
                .Select(f => ParseMetadataYml(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f)))
                .Where(m => m.Name.Length > 0)
                .OrderBy(m => m.Package)
                .ToList()
            : new List<Meta>();

        var apps = metas.Select(m => new
        {
            packageName = m.Package,
            name = m.Name,
            summary = m.Summary,
            description = m.Description,
            license = m.License,
        }).ToArray();

        var indexJson = JsonSerializer.Serialize(new
        {
            repo = new { name = "Test Repo", address = "http://localhost/fdroid/repo", timestamp = 0 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps,
            packages = new Dictionary<string, object[]>(),
        });
        File.WriteAllText(Path.Combine(repoDir, "index-v1.json"), indexJson);

        var xml = "<fdroid>" + string.Concat(metas.Select(m =>
            $"<application id=\"{EscapeXml(m.Package)}\">" +
            $"<name>{EscapeXml(m.Name)}</name>" +
            $"<summary>{EscapeXml(m.Summary)}</summary>" +
            $"<desc>{EscapeXml(m.Description)}</desc></application>")) + "</fdroid>";
        File.WriteAllText(Path.Combine(repoDir, "index.xml"), xml);
    }

    private static Meta ParseMetadataYml(string package, string yaml)
    {
        string name = "", summary = "", description = "", license = "";
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == "Description: |")
            {
                var sb = new StringBuilder();
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ", StringComparison.Ordinal))
                {
                    i++;
                    sb.AppendLine(lines[i][2..]);
                }
                description = sb.ToString().TrimEnd('\n');
                continue;
            }
            var idx = line.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var val = Unquote(line[(idx + 1)..].Trim());
            switch (key)
            {
                case "Name": name = val; break;
                case "Summary": summary = val; break;
                case "Description": description = val; break;
                case "License": license = val; break;
            }
        }
        return new Meta(package, name, summary, description, license);
    }

    private static string Unquote(string v)
        => v.Length >= 2 && v.StartsWith('"') && v.EndsWith('"')
            ? v[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
            : v;

    private static string EscapeXml(string s)
        => s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
