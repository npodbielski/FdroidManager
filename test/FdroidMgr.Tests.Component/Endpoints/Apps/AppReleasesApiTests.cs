using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Apps;

public class AppReleasesApiTests
{
    private const string KeyFp = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private sealed class StubApkInspector : IApkInspector
    {
        public string PackageName = "org.example.app";
        public int VersionCode = 2;
        private readonly string _versionName = "2.0.0";
        private readonly string _sha256 = "ab".PadRight(64, 'c');

        public Task<ApkMetadata> ReadAsync(string apkPath, CancellationToken ct = default)
            => Task.FromResult(new ApkMetadata
            {
                PackageName = PackageName,
                VersionCode = VersionCode,
                VersionName = _versionName,
                MinSdk = 21,
                TargetSdk = 34,
                Label = "Release App",
                Permissions = ["android.permission.INTERNET"],
                Sha256 = _sha256,
            });

        public ApkSignature VerifySignature(string apkPath) => Signature;

        public ApkSignature Signature { get; set; } = new(ApkSignatureStatus.Unsigned);
    }

    private sealed class FakeKeyService()
        : KeyService(null!, null!, TestIo.Real, new FakeEnvironmentService())
    {
        public override Task<(AppKey Key, string Password)> GetAvailableKeyAsync(string packageName, string? branch = null, CancellationToken ct = default)
            => Task.FromResult<(AppKey Key, string Password)>((new AppKey
            {
                PackageName = packageName,
                Alias = "release",
                StorePath = "release.keystore",
                FingerprintSha256 = KeyFp,
                PasswordEncrypted = "iv:cipher",
            }, "pw"));

        public override Task SignApkAsync(string packageName, string unsignedPath, string signedPath, string? branch = null, CancellationToken ct = default)
        {
            File.Copy(unsignedPath, signedPath, overwrite: true);
            return Task.CompletedTask;
        }
    }

    private sealed class Box<T>(T? value)
    {
        public T? Value = value;
    }

    [Fact]
    public async Task Upload_Returns200_AndInvokesFdroidUpdateWithRenameApks()
    {
        var indexJson = new Box<string?>(null);
        var (repoRoot, runner, _, factory) = await BootAsync(r =>
        {
            r.Handler = (exe, args, dir, _) =>
            {
                if (exe == "fdroid" && args.Contains("update"))
                {
                    Directory.CreateDirectory(Path.Combine(dir, "repo"));
                    if (indexJson.Value is not null)
                    {
                        File.WriteAllText(Path.Combine(dir, "repo", "index-v1.json"), indexJson.Value);
                    }

                    return new CommandResult(0, "update ok", "");
                }
                return new CommandResult(0, "", "");
            };
        });
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            var sha = "ab".PadRight(64, 'c');
            indexJson.Value = V1Index(pkg, (2, "2.0.0", $"{pkg}_2.apk", sha, 1234));

            var res = await UploadAsync(c, pkg, changelog: "what changed");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("packageName").GetString().Should().Be(pkg);
            body.GetProperty("versionCode").GetInt32().Should().Be(2);
            body.GetProperty("apkFileName").GetString().Should().Be($"{pkg}_2.apk");
            body.GetProperty("changelog").GetString().Should().Be("what changed");

            runner.Log.Should().Contain(i => i.Exe == "fdroid" && i.Args.Contains("update") && i.Args.Contains("--rename-apks"));

            File.Exists(Path.Combine(repoRoot, "repo", "app.apk")).Should().BeTrue();

            File.Exists(Path.Combine(repoRoot, "metadata", pkg, "en-US", "changelogs", "2.txt")).Should().BeTrue();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_VersionCodeReuse_Returns409()
    {
        var (repoRoot, _, inspector, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
            File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v1.json"),
                V1Index(pkg, (2, "2.0.0", $"{pkg}_2.apk", "ab".PadRight(64, 'c'), 1234)));

            inspector.VersionCode = 2;

            var res = await UploadAsync(c, pkg);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("already published");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_WrongPackage_Returns400()
    {
        var (repoRoot, _, inspector, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            inspector.PackageName = "org.example.other";

            var res = await UploadAsync(c, pkg);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("does not match app");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_BrokenSignature_Returns400()
    {
        var (repoRoot, _, inspector, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            inspector.Signature = new ApkSignature(ApkSignatureStatus.Broken, Detail: "DOES NOT VERIFY");

            var res = await UploadAsync(c, pkg);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("does not verify");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_ForeignSignature_Returns400()
    {
        var (repoRoot, _, inspector, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            inspector.Signature = new ApkSignature(ApkSignatureStatus.Verified, "ff".PadRight(64, 'f'));

            var res = await UploadAsync(c, pkg);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("expected app key");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_UnknownApp_Returns404()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var res = await UploadAsync(c, "org.example.app");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_RequiresAuth()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await UploadAsync(factory.CreateClient(), "org.example.app");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Delete_RemovesApk_AndListingShrinks()
    {
        var indexJson = new Box<string?>(null);
        var (repoRoot, _, _, factory) = await BootAsync(r =>
        {
            r.Handler = (exe, args, dir, _) =>
            {
                if (exe == "fdroid" && args.Contains("update"))
                {
                    Directory.CreateDirectory(Path.Combine(dir, "repo"));
                    if (indexJson.Value is not null)
                    {
                        File.WriteAllText(Path.Combine(dir, "repo", "index-v1.json"), indexJson.Value);
                    }

                    return new CommandResult(0, "update ok", "");
                }
                return new CommandResult(0, "", "");
            };
        });
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
            var apk = Path.Combine(repoRoot, "repo", $"{pkg}_2.apk");
            File.WriteAllBytes(apk, "PK"u8.ToArray());
            File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v1.json"),
                V1Index(pkg, (2, "2.0.0", $"{pkg}_2.apk", "ab".PadRight(64, 'c'), 1234)));

            indexJson.Value = V1Index(pkg);

            var del = await c.DeleteAsync($"/api/apps/{pkg}/releases/2");
            del.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(apk).Should().BeFalse("the APK should be removed");

            var list = await c.GetAsync($"/api/apps/{pkg}/releases");
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement
                .GetArrayLength().Should().Be(0);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Upload_FdroidUpdateFails_Returns502()
    {
        var (repoRoot, _, _, factory) = await BootAsync(r => r.SimulateUpdateFailure = true);
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            var res = await UploadAsync(c, pkg);
            res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("fdroid update failed");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task NextVersion_IncrementsPreviousRelease()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);
            await SeedSingleReleaseAsync(repoRoot, pkg);

            var res = await c.GetAsync($"/api/apps/{pkg}/releases/next-version");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("packageName").GetString().Should().Be(pkg);
            body.GetProperty("previousVersionName").GetString().Should().Be("1.0.0");
            body.GetProperty("previousVersionCode").GetInt32().Should().Be(1);
            body.GetProperty("versionName").GetString().Should().Be("1.0.1");
            body.GetProperty("versionCode").GetInt32().Should().Be(2);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task NextVersion_NoPreviousRelease_SuggestsFirst()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);

            var res = await c.GetAsync($"/api/apps/{pkg}/releases/next-version");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("previousVersionName").ValueKind.Should().Be(JsonValueKind.Null);
            body.GetProperty("previousVersionCode").ValueKind.Should().Be(JsonValueKind.Null);
            body.GetProperty("versionName").GetString().Should().Be("1.0.0");
            body.GetProperty("versionCode").GetInt32().Should().Be(1);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task NextVersion_UnknownApp_Returns404()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            (await c.GetAsync("/api/apps/org.example.app/releases/next-version"))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task NextVersion_RequiresAuth()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await factory.CreateClient().GetAsync("/api/apps/org.example.app/releases/next-version");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task List_RequiresAuth_AndReturns404ForUnknownApp()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var unauth = await factory.CreateClient().GetAsync("/api/apps/org.example.app/releases");
            unauth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var c = Auth(factory);
            (await c.GetAsync("/api/apps/org.example.app/releases")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PatchChangelog_Set_WritesSidecar_AndListsIt()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);
            await SeedSingleReleaseAsync(repoRoot, pkg);

            var res = await PatchChangelogAsync(c, pkg, 1, "first release");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("changelog").GetString().Should().Be("first release");

            var path = ChangelogSidecar(repoRoot, pkg, 1);
            File.Exists(path).Should().BeTrue();
            (await File.ReadAllTextAsync(path)).Should().Be("first release");

            var list = await c.GetAsync($"/api/apps/{pkg}/releases");
            JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement
                .EnumerateArray().First().GetProperty("changelog").GetString().Should().Be("first release");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PatchChangelog_Update_ReplacesContent()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);
            await SeedSingleReleaseAsync(repoRoot, pkg);

            (await PatchChangelogAsync(c, pkg, 1, "version one")).StatusCode.Should().Be(HttpStatusCode.OK);
            var res = await PatchChangelogAsync(c, pkg, 1, "version one — updated");
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            (await File.ReadAllTextAsync(ChangelogSidecar(repoRoot, pkg, 1)))
                .Should().Be("version one — updated");
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("changelog").GetString().Should().Be("version one — updated");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PatchChangelog_Null_RemovesSidecar()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);
            await SeedSingleReleaseAsync(repoRoot, pkg);

            (await PatchChangelogAsync(c, pkg, 1, "temporary")).StatusCode.Should().Be(HttpStatusCode.OK);
            var path = ChangelogSidecar(repoRoot, pkg, 1);
            File.Exists(path).Should().BeTrue();

            var res = await PatchChangelogAsync(c, pkg, 1, null);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            File.Exists(path).Should().BeFalse("clearing deletes the sidecar");
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("changelog").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PatchChangelog_UnknownVersion_Returns404()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.app";
            await CreateAppAsync(c, pkg);
            await SeedSingleReleaseAsync(repoRoot, pkg);

            var res = await PatchChangelogAsync(c, pkg, 999, "nope");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("release not found");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PatchChangelog_UnknownPackage_Returns404()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var res = await PatchChangelogAsync(c, "org.example.unknown", 1, "nope");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task PatchChangelog_RequiresAuth()
    {
        var (repoRoot, _, _, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await PatchChangelogAsync(factory.CreateClient(), "org.example.app", 1, "x");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static Task<(string repoRoot, FakeCommandRunner runner, StubApkInspector inspector, WebApplicationFactory<Program> factory)>
        BootAsync(Action<FakeCommandRunner>? configure = null)
    {
        var inspector = new StubApkInspector();
        var keyService = new FakeKeyService();

        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-rel.db",
            DirPrefix = "fdroidmgr-api-rel",
            Runner = configure,
            Services = services =>
            {
                services.RemoveAll<IApkInspector>();
                services.AddSingleton<IApkInspector>(inspector);
                services.RemoveAll<IKeyService>();
                services.AddSingleton<IKeyService>(keyService);
            },
        });
        host.Start();
        return Task.FromResult((host.Root, host.Runner, inspector, host.Factory));
    }

    private static HttpClient Auth(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuth.Header, "admin");
        return c;
    }

    private static async Task CreateAppAsync(HttpClient client, string package)
    {
        var res = await client.PostAsync("/api/apps", Json(new
        {
            packageName = package,
            name = "Release App",
            summary = "s",
            license = "MIT",
            generateKey = false,
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string package, string? changelog = null)
    {
        using var content = new MultipartFormDataContent();
        var apkBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 };
        var file = new ByteArrayContent(apkBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "apk", "app.apk");
        if (changelog is not null)
        {
            content.Add(new StringContent(changelog), "changelog");
        }

        return await client.PostAsync($"/api/apps/{package}/releases", content);
    }

    private static string V1Index(string package, params (int vc, string vn, string apkName, string sha, long size)[] versions)
    {
        var apps = new object[]
        {
            new
            {
                packageName = package,
                name = "Release App",
                license = "MIT",
                suggestedVersionCode = versions.Length > 0 ? versions.Max(v => v.vc).ToString() : "2147483647",
            },
        };
        var packages = new Dictionary<string, object[]>
        {
            [package] =
            [
                .. versions.Select(v => new
                {
                    versionCode = v.vc,
                    versionName = v.vn,
                    v.apkName,
                    hash = v.sha,
                    hashType = "sha256",
                    v.size,
                    minSdkVersion = 21,
                    targetSdkVersion = 34,
                })
            ],
        };
        return JsonSerializer.Serialize(new
        {
            repo = new { name = "Test", address = "http://localhost/fdroid/repo", timestamp = 0 },
            requests = new { install = Array.Empty<object>(), uninstall = Array.Empty<object>() },
            apps,
            packages,
        });
    }

    private static async Task<HttpResponseMessage> PatchChangelogAsync(HttpClient client, string package, int versionCode, string? changelog)
        => await client.PatchAsync($"/api/apps/{package}/releases/{versionCode}",
            Json(new { changelog }));

    private static string ChangelogSidecar(string repoRoot, string pkg, int versionCode)
        => Path.Combine(repoRoot, "metadata", pkg, "en-US", "changelogs", versionCode + ".txt");

    private static async Task SeedSingleReleaseAsync(string repoRoot, string pkg)
    {
        Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "repo", "index-v1.json"),
            V1Index(pkg, (1, "1.0.0", $"{pkg}_1.apk", "ab".PadRight(64, 'c'), 1234)));
    }
}
