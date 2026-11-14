using System.Net;
using System.Text.Json;
using FdroidMgr.Api.Models;
using FluentAssertions;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component;

/// <summary>
/// Every endpoint that answers with a typed response record must expose that
/// record in the OpenAPI document; this is the guard against slipping back to
/// anonymous bodies, which the document renders as an empty schema.
/// </summary>
public class OpenApiDocumentTests
{
    private static readonly (string Path, string Operation, int Status, string Schema)[] TypedResponses =
    [
        ("/healthz", "get", 200, "HealthzResponse"),
        ("/api/auth/me", "get", 200, "AuthMeResponse"),
        ("/api/apps", "get", 200, "AppListItem"),
        ("/api/apps", "post", 201, "AppCreatedResponse"),
        ("/api/apps/{packageName}", "get", 200, "AppDetailResponse"),
        ("/api/apps/{packageName}", "put", 200, "AppUpdatedResponse"),
        ("/api/apps/{packageName}/icon", "post", 200, "AppIconResponse"),
        ("/api/apps/{packageName}/metadata", "get", 200, "AppMetadataResponse"),
        ("/api/apps/{packageName}/metadata", "post", 200, "AppMetadataWrittenResponse"),
        ("/api/apps/{packageName}/keys", "get", 200, "AppKeyResponse"),
        ("/api/apps/{packageName}/keys", "post", 200, "AppKeyResponse"),
        ("/api/apps/{packageName}/keys/import", "post", 200, "AppKeyResponse"),
        ("/api/apps/{packageName}/releases", "get", 200, "ReleaseDto"),
        ("/api/apps/{packageName}/releases", "post", 200, "ReleaseDto"),
        ("/api/apps/{packageName}/releases/next-version", "get", 200, "NextReleaseVersionDto"),
        ("/api/apps/{packageName}/releases/{versionCode}", "patch", 200, "ReleaseDto"),
        ("/api/apps/{packageName}/releases/{versionCode}", "delete", 502, "DeleteReleaseFailureResponse"),
        ("/api/api-keys", "get", 200, "ApiKeyListItem"),
        ("/api/api-keys", "post", 200, "ApiKeyCreatedResponse"),
        ("/api/fdroid-client-key", "get", 200, "ClientKeyStateResponse"),
        ("/api/fdroid-client-key/secret", "get", 200, "ClientKeySecretResponse"),
        ("/api/fdroid-client-key/regenerate", "post", 200, "ClientKeySecretResponse"),
        ("/api/stats/downloads", "get", 200, "DownloadStatsDto"),
        ("/api/repo", "get", 200, "RepoConfig"),
        ("/api/repo", "put", 200, "RepoConfig"),
        ("/api/repo/init", "post", 201, "RepoInitResult"),
        ("/api/auth/oauth", "post", 201, "OAuthConfigStatus"),
        ("/api/auth/oauth/status", "get", 200, "OAuthConfigStatus"),
        ("/api/repo/index/rebuild", "post", 200, "StatusResponse"),
        ("/api/repo/init/status", "get", 200, "RepoInitStatus"),
        ("/api/repo/status", "get", 200, "RepoStatusResponse"),
        ("/api/apps/{packageName}", "get", 404, "ErrorResponse"),
    ];

    [Fact]
    public async Task OpenApiDocument_DescribesEveryTypedResponse()
    {
        using var host = new TestHost(new TestHostOptions { DbName = "openapi.db", DirPrefix = "fdroidmgr-openapi" });
        var res = await host.Client().GetAsync("/openapi/v1.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var schemas = root.GetProperty("components").GetProperty("schemas");

        foreach (var (path, operation, status, schema) in TypedResponses)
        {
            var response = paths.GetProperty(path).GetProperty(operation).GetProperty("responses")
                .GetProperty(status.ToString());
            var emitted = response.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").ToString();

            // Collection endpoints describe an array of the item schema, single ones a $ref.
            emitted.Should().Contain(schema, $"{operation.ToUpperInvariant()} {path} must describe its {schema} payload");

            schemas.TryGetProperty(schema, out _).Should().BeTrue(
                $"{schema} is referenced but has no component schema");
        }
    }

    [Fact]
    public void ClientKeySecret_OmitsRegeneratedUtcUntilTheKeyWasRegenerated()
    {
        var revealed = new ClientKeySecretResponse("k", "p", "u", "b", DateTime.UtcNow);
        TestJson.Serialize(revealed).Should().NotContain("regeneratedUtc");

        var regenerated = revealed with { RegeneratedUtc = DateTime.UtcNow };
        TestJson.Serialize(regenerated).Should().Contain("regeneratedUtc");
    }
}
