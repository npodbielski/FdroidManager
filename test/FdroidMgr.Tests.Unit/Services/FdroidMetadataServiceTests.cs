using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

[Collection(ProcessEnvSensitiveCollection.Name)]
public class FdroidMetadataServiceTests
{
    private const string Fp64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string Package = "org.example.helloworld";

    [Fact]
    public void Render_MapsEveryField_AndOmitsEmptyOptionals()
    {
        var yaml = FdroidMetadataService.Render(Fields(), [Fp64]);

        yaml.Should().Contain("Name: Hello World\n");
        yaml.Should().Contain("Summary: A demo app\n");
        yaml.Should().Contain("Description: A demo app\n");
        yaml.Should().Contain("License: MIT\n");
        yaml.Should().Contain("WebSite: https://example.com\n");
        yaml.Should().Contain("SourceCode: https://git.example.com/example/helloworld\n");
        yaml.Should().Contain("IssueTracker: https://git.example.com/example/helloworld/-/issues\n");
        yaml.Should().EndWith("\n");
    }

    [Fact]
    public void Render_OmitsOptionalKeys_WhenEmpty()
    {
        var app = Fields();
        app = app with { Website = null, SourceCode = "", IssueTracker = null };

        var yaml = FdroidMetadataService.Render(app, null);

        yaml.Should().NotContain("WebSite:");
        yaml.Should().NotContain("SourceCode:");
        yaml.Should().NotContain("IssueTracker:");
    }

    [Fact]
    public void Render_MultiLineDescription_UsesBlockScalar()
    {
        var yaml = FdroidMetadataService.Render(Fields("Line one\nLine two"), null);

        yaml.Should().Contain("Description: |\n  Line one\n  Line two\n");
    }

    [Fact]
    public void Render_DescriptionWithSurroundingWhitespace_UsesBlockScalar()
    {
        var yaml = FdroidMetadataService.Render(Fields("  padded  "), null);

        yaml.Should().Contain("Description: |\n    padded  \n");
    }

    [Fact]
    public void Render_AllowedAPKSigningKeys_LowercasedAndListed()
    {
        var yaml = FdroidMetadataService.Render(Fields(), ["ABCDEF".PadRight(64, '0')]);

        yaml.Should().Contain("AllowedAPKSigningKeys:\n  - " + "abcdef".PadRight(64, '0') + "\n");
    }

    [Fact]
    public void Render_AllowedAPKSigningKeys_OmittedWhenNone()
    {
        FdroidMetadataService.Render(Fields(), null).Should().NotContain("AllowedAPKSigningKeys:");
        FdroidMetadataService.Render(Fields(), []).Should().NotContain("AllowedAPKSigningKeys:");
        FdroidMetadataService.Render(Fields(), ["", "  "]).Should().NotContain("AllowedAPKSigningKeys:");
    }

    [Fact]
    public void EscapeYamlScalar_QuotesAmbiguousValues()
    {
        FdroidMetadataService.EscapeYamlScalar("foo: bar").Should().Be("\"foo: bar\"");
        FdroidMetadataService.EscapeYamlScalar("foo # bar").Should().Be("\"foo # bar\"");
        FdroidMetadataService.EscapeYamlScalar(" leading").Should().Be("\" leading\"");
        FdroidMetadataService.EscapeYamlScalar("trailing ").Should().Be("\"trailing \"");
        FdroidMetadataService.EscapeYamlScalar("\n").Should().Be("\"\\n\"");
        FdroidMetadataService.EscapeYamlScalar("").Should().Be("\"\"");
        FdroidMetadataService.EscapeYamlScalar("true").Should().Be("\"true\"");
        FdroidMetadataService.EscapeYamlScalar("- item").Should().Be("\"- item\"");
    }

    [Fact]
    public void EscapeYamlScalar_LeavesPlainValuesUnchanged()
    {
        FdroidMetadataService.EscapeYamlScalar("plain").Should().Be("plain");
        FdroidMetadataService.EscapeYamlScalar("Hello World").Should().Be("Hello World");
        FdroidMetadataService.EscapeYamlScalar("https://example.com").Should().Be("https://example.com");
    }

    [Fact]
    public void ParseAppMetadata_ReadsBackRenderedYaml()
    {
        var yaml = FdroidMetadataService.Render(Fields("a\nb"), [Fp64]);

        var parsed = FdroidMetadataService.ParseAppMetadata(yaml);

        parsed.Name.Should().Be("Hello World");
        parsed.Summary.Should().Be("A demo app");
        parsed.Description.Should().Be("a\nb");
        parsed.License.Should().Be("MIT");
        parsed.Website.Should().Be("https://example.com");
        parsed.SourceCode.Should().Be("https://git.example.com/example/helloworld");
        parsed.IssueTracker.Should().Be("https://git.example.com/example/helloworld/-/issues");
    }

    [Fact]
    public void GetPath_PointsAtMetadataUnderRepoRoot()
    {
        var svc = new FdroidMetadataService(null!, NullLogger<FdroidMetadataService>.Instance, TestIo.Real, new FakeEnvironmentService());
        svc.GetPath(Package, "/data/fdroidrepo")
            .Should().Be(Path.Combine("/data/fdroidrepo", "metadata", "org.example.helloworld.yml"));
        svc.GetPath(Package, "").Should().BeNull();
        svc.GetPath(Package, "  ").Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_WritesUnderRepoRoot_NotTheOutputRepoDir()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fdroidmgr-meta-" + Guid.NewGuid().ToString("N"));
        var repoDir = Path.Combine(tmp, "repo");
        Directory.CreateDirectory(repoDir);
        var previousEnv = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);

            var db = TestDb.Create();
            await TestDb.SeedRepoConfigAsync(db, repoDir, Path.Combine(tmp, "keys"));
            await TestDb.SeedKeyAsync(db, Package, Fp64);

            var svc = new FdroidMetadataService(db, NullLogger<FdroidMetadataService>.Instance, TestIo.Real, new FakeEnvironmentService());
            var path = await svc.WriteAsync(Package, Fields(), TestContext.Current.CancellationToken);

            path.Should().Be(Path.Combine(tmp, "metadata", "org.example.helloworld.yml"));
            File.Exists(path).Should().BeTrue();

            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("Name: Hello World");
            content.Should().Contain("AllowedAPKSigningKeys:\n  - " + Fp64);

            (await svc.ReadAsync(Package, TestContext.Current.CancellationToken)).Should().Be(content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", previousEnv);
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task WriteAsync_IsIdempotent_Overwrites()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fdroidmgr-meta-" + Guid.NewGuid().ToString("N"));
        var repoDir = Path.Combine(tmp, "repo");
        Directory.CreateDirectory(repoDir);
        var previousEnv = Environment.GetEnvironmentVariable("FDROID_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", null);

            var db = TestDb.Create();
            await TestDb.SeedRepoConfigAsync(db, repoDir, Path.Combine(tmp, "keys"));
            var svc = new FdroidMetadataService(db, NullLogger<FdroidMetadataService>.Instance, TestIo.Real, new FakeEnvironmentService());

            var first = await svc.WriteAsync(Package, Fields(), TestContext.Current.CancellationToken);
            File.WriteAllText(first, "stale content that must be overwritten");

            var second = await svc.WriteAsync(Package, Fields(), TestContext.Current.CancellationToken);
            second.Should().Be(first);
            var content = await File.ReadAllTextAsync(second);
            content.Should().StartWith("Name: Hello World");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDROID_REPO_ROOT", previousEnv);
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenNotWritten()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fdroidmgr-meta-" + Guid.NewGuid().ToString("N"));
        var repoDir = Path.Combine(tmp, "repo");
        Directory.CreateDirectory(repoDir);
        try
        {
            var db = TestDb.Create();
            await TestDb.SeedRepoConfigAsync(db, repoDir, Path.Combine(tmp, "keys"));
            var svc = new FdroidMetadataService(db, NullLogger<FdroidMetadataService>.Instance, TestIo.Real, new FakeEnvironmentService());

            (await svc.ReadAsync("org.example.nope", TestContext.Current.CancellationToken)).Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    private static AppMetadataFields Fields(string description = "A demo app") => new()
    {
        Name = "Hello World",
        Summary = "A demo app",
        Description = description,
        License = "MIT",
        Website = "https://example.com",
        SourceCode = "https://git.example.com/example/helloworld",
        IssueTracker = "https://git.example.com/example/helloworld/-/issues",
    };
}
