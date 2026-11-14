using System.Net;
using System.Text;
using System.Text.Json;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component;

public class ProblemDetailsTests
{
    private sealed class ExplodingReleaseService : IReleaseService
    {
        public Task<ReleaseDto> PublishAsync(string packageName, IFormFile apk, string? changelog, string? branch = null, CancellationToken ct = default)
            => throw new NotSupportedException("boom from the test service");
    }

    [Fact]
    public async Task UnknownException_Returns500ProblemBody_AndDoesNotRethrow()
    {
        var host = new TestHost(new TestHostOptions
        {
            DbName = "problem.db",
            DirPrefix = "fdroidmgr-problem",
            Services = services =>
            {
                services.RemoveAll<IReleaseService>();
                services.AddSingleton<IReleaseService>(new ExplodingReleaseService());
            },
        });
        try
        {
            var c = host.Client(signedInAs: "admin");
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "apk", "app.apk");

            var res = await c.PostAsync("/api/apps/org.example.app/releases", form);

            res.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            var raw = await res.Content.ReadAsStringAsync();
            var body = JsonDocument.Parse(raw).RootElement;
            body.GetProperty("status").GetInt32().Should().Be(500);
            body.GetProperty("error").GetString().Should().Be("An error occurred while processing the request");
            raw.Should().NotContain("boom", "the exception message is not leaked to clients");
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task ValidationProblem_BodyKeepsFieldErrors()
    {
        var host = new TestHost(new TestHostOptions { DbName = "problem2.db", DirPrefix = "fdroidmgr-problem2" });
        try
        {
            var c = host.Client(signedInAs: "admin");
            var res = await c.PostAsync("/api/api-keys",
                new StringContent("""{"name":"bad","scopes":["admin"]}""", Encoding.UTF8, "application/json"));

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("error").GetString().Should().Contain("invalid scope");
            body.TryGetProperty("errors", out _).Should().BeTrue("field errors survive the central mapper");
        }
        finally
        {
            host.Dispose();
        }
    }
}
