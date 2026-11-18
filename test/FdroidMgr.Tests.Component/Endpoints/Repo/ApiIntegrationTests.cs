using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Api.Models;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.Repo;

public class ApiIntegrationTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task RepoEndpoint_ServesIndex_AndAppliesAuthOnManagement()
    {
        // the repository is gated by default: authenticated serves, anonymous does not
        var gated = await fx.Client.GetAsync("/fdroid/repo/index.xml");
        gated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var pub = await fx.Authenticated().GetAsync("/fdroid/repo/index.xml");
        pub.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pub.Content.ReadAsStringAsync()).Should().Contain("<fdroid");

        var unauth = await fx.Client.GetAsync("/api/apps");
        unauth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LocalLogin_IsRemoved()
    {
        var res = await fx.Client.PostAsync("/api/auth/login",
            Json(new { username = "admin", password = "wrong" }));
        res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task FullFlow_CreateApp_PublishRelease_ServeApk()
    {
        var client = fx.Authenticated();

        var pkg = "org.integration.demo" + Guid.NewGuid().ToString("N")[..8];

        var create = await client.PostAsync("/api/apps", Json(new
        {
            packageName = pkg,
            name = "Integration App",
            summary = "summary",
            license = "MIT",
            generateKey = false,
        }));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement
            .GetProperty("packageName").GetString().Should().Be(pkg);

        using var content = new MultipartFormDataContent();
        var apkBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 };
        var fileContent = new ByteArrayContent(apkBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "apk", "app.apk");
        content.Add(new StringContent("first"), "changelog");

        var publish = await client.PostAsync($"/api/apps/{pkg}/releases", content);
        publish.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);

        using var iconContent = new MultipartFormDataContent();
        var iconBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var iconFile = new ByteArrayContent(iconBytes);
        iconFile.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        iconContent.Add(iconFile, "file", "icon.png");
        var iconRes = await client.PostAsync($"/api/apps/{pkg}/icon", iconContent);
        iconRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RepoEndpoint_PreventsPathTraversal()
    {
        var res = await fx.Client.GetAsync("/fdroid/repo/../../etc/passwd");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AppCrud_CreateDuplicateUpdateDelete_KeyedByPackageName()
    {
        var c = fx.Authenticated();
        var pkg = "org.crud.demo" + Guid.NewGuid().ToString("N")[..6];
        await CreateAppAsync(c, pkg);

        var dup = await c.PostAsync("/api/apps", Json(new { packageName = pkg, name = "x", generateKey = false }));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var upd = await c.PutAsync($"/api/apps/{pkg}", Json(new { name = "Renamed", license = "Apache-2.0" }));
        upd.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await upd.Content.ReadAsAsync<AppUpdatedResponse>();
        updated.PackageName.Should().Be(pkg);
        updated.Name.Should().Be("Renamed");
        updated.License.Should().Be("Apache-2.0");

        var del = await c.DeleteAsync($"/api/apps/{pkg}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AppNotFound_Returns404()
    {
        var c = fx.Authenticated();
        var res = await c.GetAsync("/api/apps/org.nope.doesnotexist");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Keys_GenerateAndList()
    {
        var c = fx.Authenticated();
        var pkg = "org.keys.demo" + Guid.NewGuid().ToString("N")[..6];
        await CreateAppAsync(c, pkg);

        var res = await c.PostAsync($"/api/apps/{pkg}/keys", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var generated = await res.Content.ReadAsAsync<AppKeyResponse>();
        generated.Alias.Should().NotBeNullOrEmpty();
        generated.PackageName.Should().Be(pkg);
        generated.FingerprintSha256.Should().NotBeNullOrEmpty();

        var list = await c.GetAsync($"/api/apps/{pkg}/keys");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement
            .GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RepoConfig_GetAndUpdate_RegeneratesIndex()
    {
        var c = fx.Authenticated();

        var get = await c.GetAsync("/api/repo");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var cfg = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement;
        cfg.GetProperty("repoName").GetString().Should().NotBeNullOrEmpty();

        var put = await c.PutAsync("/api/repo", Json(new { repoName = "Updated Name" }));
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get2 = await c.GetAsync("/api/repo");
        get2.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await get2.Content.ReadAsStringAsync()).RootElement
            .GetProperty("repoName").GetString().Should().Be("Updated Name");
    }

    [Fact]
    public async Task Status_IsPublic()
    {
        var c = fx.Authenticated();
        (await c.PostAsync("/api/repo/index/rebuild", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var res = await fx.Client.GetAsync("/api/repo/status");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("repoName").GetString().Should().NotBeNullOrEmpty();

        body.GetProperty("indexHealthy").GetBoolean().Should().BeTrue();
        body.GetProperty("lastIndexError").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task CreateAppAsync(HttpClient c, string pkg)
    {
        var res = await c.PostAsync("/api/apps", Json(new
        {
            packageName = pkg,
            name = "App " + pkg,
            summary = "s",
            license = "MIT",
            generateKey = false,
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
            .GetProperty("packageName").GetString().Should().Be(pkg);
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
}
