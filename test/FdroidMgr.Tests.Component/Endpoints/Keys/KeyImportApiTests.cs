using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using FdroidMgr.Tests.Unit.Fixtures;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Keys;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class KeyImportApiTests
{
    private const string MasterKey = "api-test-master-key-0123456789abcdef";

    [Fact]
    public async Task Import_WithNoReleases_Returns200_WritesRowAndKeystore()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.keyimport.new";
            await CreateAppAsync(c, pkg);

            var (bytes, password, fingerprint) = CreateKeystore();

            var res = await ImportAsync(c, pkg, bytes, password);

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("packageName").GetString().Should().Be(pkg);
            body.GetProperty("alias").GetString().Should().Be("release");
            body.GetProperty("branch").ValueKind.Should().Be(JsonValueKind.Null);
            body.GetProperty("fingerprintSha256").GetString().Should().Be(fingerprint);
            body.GetProperty("storePath").GetString().Should().Be(Path.Combine(pkg, "release.keystore"));

            var keys = await ListKeysAsync(c, pkg);
            keys.Should().ContainSingle();
            keys[0].GetProperty("fingerprintSha256").GetString().Should().Be(fingerprint);

            File.Exists(Path.Combine(repoRoot, "keys", pkg, "release.keystore")).Should().BeTrue();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Import_WithMatchingSigner_Returns200()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.keyimport.match";
            await CreateAppAsync(c, pkg);

            var (bytes, password, fingerprint) = CreateKeystore();

            Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
            File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v1.json"),
                V1Index(pkg, (1, "1.0.0", $"{pkg}_1.apk", "ab".PadRight(64, 'c'), 1234, fingerprint)));

            var res = await ImportAsync(c, pkg, bytes, password);

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("fingerprintSha256").GetString().Should().Be(fingerprint);

            (await ListKeysAsync(c, pkg)).Should().ContainSingle();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Import_WithMismatchedSigner_Returns409_AndWritesNoRow()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.keyimport.mismatch";
            await CreateAppAsync(c, pkg);

            var (bytes, password, importedFp) = CreateKeystore();
            var recordedFp = "beef".PadRight(64, '0');

            Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
            File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v1.json"),
                V1Index(pkg, (1, "1.0.0", $"{pkg}_1.apk", "ab".PadRight(64, 'c'), 1234, recordedFp)));

            var res = await ImportAsync(c, pkg, bytes, password);

            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain(importedFp);
            body.GetProperty("error").GetString().Should().Contain(recordedFp);

            (await ListKeysAsync(c, pkg)).Should().BeEmpty();
            File.Exists(Path.Combine(repoRoot, "keys", pkg, "release.keystore")).Should().BeFalse();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Import_WrongPassword_Returns400()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.keyimport.badpass";
            await CreateAppAsync(c, pkg);

            var (bytes, _, _) = CreateKeystore();

            var res = await ImportAsync(c, pkg, bytes, "DefinitelyWrongPassword!");

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("unreadable keystore");

            (await ListKeysAsync(c, pkg)).Should().BeEmpty();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Generate_WithReleases_Returns409_AndDoesNotOverwriteKeystore()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.keygen.guard";
            await CreateAppAsync(c, pkg);

            Directory.CreateDirectory(Path.Combine(repoRoot, "repo"));
            File.WriteAllText(Path.Combine(repoRoot, "repo", "index-v1.json"),
                V1Index(pkg, (1, "1.0.0", $"{pkg}_1.apk", "ab".PadRight(64, 'c'), 1234, "beef".PadRight(64, '0'))));

            var storeDir = Path.Combine(repoRoot, "keys", pkg);
            Directory.CreateDirectory(storeDir);
            var storeFile = Path.Combine(storeDir, "release.keystore");
            var sentinel = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            File.WriteAllBytes(storeFile, sentinel);

            var res = await c.PostAsync($"/api/apps/{pkg}/keys", null);

            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Contain("/keys/import");

            (await ListKeysAsync(c, pkg)).Should().BeEmpty();
            File.ReadAllBytes(storeFile).Should().Equal(sentinel);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Generate_WithoutReleases_Returns200()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var pkg = "org.example.keygen.fresh";
            await CreateAppAsync(c, pkg);

            var res = await c.PostAsync($"/api/apps/{pkg}/keys", null);

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("fingerprintSha256").GetString().Should().MatchRegex("^[0-9a-f]{64}$");

            (await ListKeysAsync(c, pkg)).Should().ContainSingle();
            File.Exists(Path.Combine(repoRoot, "keys", pkg, "release.keystore")).Should().BeTrue();
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Import_UnknownApp_Returns404()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var c = Auth(factory);
            var (bytes, password, _) = CreateKeystore();

            var res = await ImportAsync(c, "org.example.keyimport.unknown", bytes, password);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
                .GetProperty("error").GetString().Should().Be("app not found");
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Import_RequiresAuth()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var (bytes, password, _) = CreateKeystore();
            var res = await ImportAsync(factory.CreateClient(), "org.example.keyimport.anon", bytes, password);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Generate_RequiresAuth()
    {
        var (repoRoot, factory) = await BootAsync();
        await using var _ = factory;
        try
        {
            var res = await factory.CreateClient().PostAsync("/api/apps/org.example.keygen.anon/keys", null);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { try { Directory.Delete(repoRoot, true); } catch (IOException) { } }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static Task<(string repoRoot, WebApplicationFactory<Program> factory)> BootAsync()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "api-key.db",
            MasterKey = MasterKey,
            DirPrefix = "fdroidmgr-api-key",
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

    private static async Task CreateAppAsync(HttpClient client, string package)
    {
        var res = await client.PostAsync("/api/apps", Json(new
        {
            packageName = package,
            name = "Key Test App",
            summary = "s",
            license = "MIT",
            generateKey = false,
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static (byte[] Bytes, string Password, string Fingerprint) CreateKeystore(string password = "ImportPass123!")
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fdroidmgr-import-ks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var ks = Path.Combine(tmp, "upload.keystore");
            var psi = new ProcessStartInfo("keytool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[]
            {
                "-genkeypair", "-storetype", "PKCS12", "-keystore", ks,
                "-storepass", password, "-keypass", password, "-alias", "release",
                "-keysize", "2048", "-keyalg", "RSA", "-validity", "100",
                "-dname", "CN=KeyImportTest",
            })
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            p.ExitCode.Should().Be(0, "keytool must be able to create a keystore");

            var fp = KeyService.GetFingerprint(ks, password, "release");
            fp.Should().MatchRegex("^[0-9a-f]{64}$");
            return (File.ReadAllBytes(ks), password, fp);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    private static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client, string package, byte[] keystoreBytes, string storePassword,
        string? keyPassword = null, string? alias = null)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(keystoreBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "keystore", "upload.keystore");
        content.Add(new StringContent(storePassword), "storePassword");
        if (keyPassword is not null)
        {
            content.Add(new StringContent(keyPassword), "keyPassword");
        }

        if (alias is not null)
        {
            content.Add(new StringContent(alias), "alias");
        }

        return await client.PostAsync($"/api/apps/{package}/keys/import", content);
    }

    private static string V1Index(string package, params (int vc, string vn, string apkName, string sha, long size, string? signer)[] versions)
    {
        var apps = new object[]
        {
            new { packageName = package, name = "Key Test App", license = "MIT" },
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
                    v.signer,
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

    private static async Task<List<JsonElement>> ListKeysAsync(HttpClient client, string package)
    {
        var res = await client.GetAsync($"/api/apps/{package}/keys");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return [.. JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.EnumerateArray()];
    }
}
