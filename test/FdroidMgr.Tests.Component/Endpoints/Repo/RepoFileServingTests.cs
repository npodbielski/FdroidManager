using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using FdroidMgr.Api.Services;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class RepoFileServingTests
{
    [Fact]
    public async Task ServedFiles_SendNosniff()
    {
        var host = NewHost("serving-sniff");
        try
        {
            var res = await Auth(host).GetAsync("/fdroid/repo/com.example_3.apk");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            res.Headers.TryGetValues("X-Content-Type-Options", out var values).Should().BeTrue();
            values.Should().ContainSingle().Which.Should().Be("nosniff");
        }
        finally
        {
            Dispose(host);
        }
    }

    [Fact]
    public async Task RangeRequestOnApk_Returns206_WithContentRange()
    {
        var host = NewHost("serving-range");
        try
        {
            var client = Auth(host);
            var request = new HttpRequestMessage(HttpMethod.Get, "/fdroid/repo/com.example_3.apk");
            request.Headers.Range = new RangeHeaderValue(1, 2);

            var res = await client.SendAsync(request);

            res.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            res.Content.Headers.ContentRange!.ToString().Should().Be("bytes 1-2/4");
            (await res.Content.ReadAsByteArrayAsync()).Should().Equal(0x4B, 0x03);
        }
        finally
        {
            Dispose(host);
        }
    }

    [Theory]
    [InlineData("/fdroid/repo/../config.yml")]
    [InlineData("/fdroid/repo/..%2fconfig.yml")]
    [InlineData("/fdroid/repo/..%2fCONFIG.yml")]
    public async Task ParentDirectoryEscape_Returns404(string url)
    {
        var host = NewHost("serving-escape");
        try
        {
            var root = Directory.GetParent(host.RepoDir)!.FullName;
            File.WriteAllText(Path.Combine(root, "config.yml"), "keystore: secret\n");
            File.WriteAllText(Path.Combine(root, "CONFIG.yml"), "keystore: secret\n");
            File.WriteAllText(Path.Combine(root, "visible.txt"), "hello\n");

            (await Auth(host).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Auth(host).GetAsync(url.Replace("config.yml", "visible.txt", StringComparison.OrdinalIgnoreCase)))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            Dispose(host);
        }
    }

    [Theory]
    [InlineData("config.yml")]
    [InlineData("repo.p12")]
    [InlineData("repo.jks")]
    [InlineData("fdroidmgr.db")]
    [InlineData(".hidden")]
    public async Task DenylistInsideRepoDir_Returns404(string name)
    {
        var host = NewHost("serving-deny");
        try
        {
            File.WriteAllText(Path.Combine(host.RepoDir, name), "secret\n");

            var res = await Auth(host).GetAsync("/fdroid/repo/" + name);

            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            Dispose(host);
        }
    }

    [Fact]
    public async Task OrdinaryFiles_AreStillServed()
    {
        var host = NewHost("serving-ok");
        try
        {
            File.WriteAllText(Path.Combine(host.RepoDir, "index.xml"), "<fdroid />");

            var res = await Auth(host).GetAsync("/fdroid/repo/index.xml");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Dispose(host);
        }
    }

    [Fact]
    public async Task PublicDirIsSet_ServesFromIt_IgnoresTheFdroidOutputDir()
    {
        var host = NewHost("serving-public", publicDir: true);
        try
        {
            var publicDir = Path.Combine(host.Root, "public");
            File.WriteAllText(Path.Combine(publicDir, "index.xml"), "<fdroid><repo name=\"Public\" /></fdroid>");

            var pub = await Auth(host).GetAsync("/fdroid/repo/index.xml");
            pub.StatusCode.Should().Be(HttpStatusCode.OK);
            (await pub.Content.ReadAsStringAsync()).Should().Contain("Public");

            var secret = await Auth(host).GetAsync("/fdroid/repo/com.example_3.apk");
            secret.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            Dispose(host);
        }
    }

    [Fact]
    public async Task RebuildStagesTheOutputDirSoServingNeverReadsIt()
    {
        var host = NewHost("serving-stage", publicDir: true, runner: r => r.Handler = (exe, _, workDir, _) =>
        {
            if (exe == "fdroid")
            {
                Directory.CreateDirectory(Path.Combine(workDir, "repo"));
                File.WriteAllText(Path.Combine(workDir, "repo", "index.xml"), "<fdroid><repo name=\"Staged\" /></fdroid>");
                File.WriteAllText(Path.Combine(workDir, "repo", "config.yml"), "keystore: secret\n");
            }

            return new CommandResult(0, "update ok", "");
        });

        try
        {
            var c = Auth(host);

            (await c.PostAsync("/api/repo/index/rebuild", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            var served = await c.GetAsync("/fdroid/repo/index.xml");
            served.StatusCode.Should().Be(HttpStatusCode.OK);
            (await served.Content.ReadAsStringAsync()).Should().Contain("Staged");
            (await c.GetAsync("/fdroid/repo/config.yml")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            Dispose(host);
        }
    }

    [Fact]
    public async Task ServedFiles_GetTheirContentType()
    {
        var host = NewHost("serving-types");
        try
        {
            File.WriteAllText(Path.Combine(host.RepoDir, "icon.svg"), "<svg/>");
            File.WriteAllText(Path.Combine(host.RepoDir, "index.jar"), "jar");
            File.WriteAllText(Path.Combine(host.RepoDir, "index.css"), "a{}");
            File.WriteAllText(Path.Combine(host.RepoDir, "index.html"), "<html/>");
            File.WriteAllText(Path.Combine(host.RepoDir, "notes.bin"), "bin");
            var client = Auth(host);

            var expected = new Dictionary<string, string>
            {
                ["icon.svg"] = "image/svg+xml",
                ["index.jar"] = "application/java-archive",
                ["index.css"] = "text/css",
                ["index.html"] = "text/html",
                ["notes.bin"] = "application/octet-stream",
            };

            foreach (var (name, mediaType) in expected)
            {
                var res = await client.GetAsync("/fdroid/repo/" + name);
                res.StatusCode.Should().Be(HttpStatusCode.OK);
                res.Content.Headers.ContentType!.MediaType.Should().Be(mediaType);
            }
        }
        finally
        {
            Dispose(host);
        }
    }

    private static TestHost NewHost(string dirPrefix, bool publicDir = false, Action<FakeCommandRunner>? runner = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "fdroidmgr-" + dirPrefix + "-" + Guid.NewGuid().ToString("N"));
        var host = new TestHost(new TestHostOptions
        {
            DbName = "serving.db",
            DirPrefix = "fdroidmgr-" + dirPrefix,
            Root = root,
            Runner = runner,
            Settings = publicDir
                ? new Dictionary<string, string?> { ["FDROID_REPO_PUBLIC_PATH"] = Path.Combine(root, "public") }
                : null,
        });

        if (publicDir)
        {
            Directory.CreateDirectory(Path.Combine(root, "public"));
        }

        File.WriteAllBytes(Path.Combine(host.RepoDir, "com.example_3.apk"), [0x50, 0x4B, 0x03, 0x04]);
        return host;
    }

    private static HttpClient Auth(TestHost host) => host.Client("admin");

    private static void Dispose(TestHost host) => host.Dispose();
}
