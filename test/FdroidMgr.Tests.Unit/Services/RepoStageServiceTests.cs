using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class RepoStageServiceTests : IDisposable
{
    private readonly string _root;

    public RepoStageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fdroidmgr-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Stage_CopiesPublicFiles_AndSkipsSecrets()
    {
        var (source, target) = WriteRepo();
        var svc = Create(target);

        svc.Stage(new RepoConfig { RepoPath = source });

        File.ReadAllText(Path.Combine(target, "index.xml")).Should().Contain("<fdroid");
        File.Exists(Path.Combine(target, "com.example_3.apk")).Should().BeTrue();
        File.Exists(Path.Combine(target, "icons-320", "com.example.png")).Should().BeTrue();
        File.Exists(Path.Combine(target, "config.yml")).Should().BeFalse();
        File.Exists(Path.Combine(target, "repo.p12")).Should().BeFalse();
        File.Exists(Path.Combine(target, "secrets", "app.jks")).Should().BeFalse();
        File.Exists(Path.Combine(target, ".hidden")).Should().BeFalse();
    }

    [Fact]
    public void Stage_IsIncremental_AndPrunesFilesGoneFromSource()
    {
        var (source, target) = WriteRepo();
        var svc = Create(target);
        var cfg = new RepoConfig { RepoPath = source };

        svc.Stage(cfg);

        var stamp = File.GetLastWriteTimeUtc(Path.Combine(target, "com.example_3.apk"));
        svc.Stage(cfg);
        File.GetLastWriteTimeUtc(Path.Combine(target, "com.example_3.apk")).Should().Be(stamp,
            "an unchanged file must not be copied again");

        File.WriteAllBytes(Path.Combine(source, "com.example_3.apk"), [0x50, 0x4B, 0x03, 0x04, 0x00, 0x01]);
        svc.Stage(cfg);
        new FileInfo(Path.Combine(target, "com.example_3.apk")).Length.Should().Be(6);

        File.Delete(Path.Combine(source, "com.example_3.apk"));
        svc.Stage(cfg);
        File.Exists(Path.Combine(target, "com.example_3.apk")).Should().BeFalse();
        File.Exists(Path.Combine(target, "index.xml")).Should().BeTrue();
    }

    [Fact]
    public void Stage_CopiesAgainWhenAFileShrinksToTheSameLength()
    {
        var (source, target) = WriteRepo();
        var svc = Create(target);
        var cfg = new RepoConfig { RepoPath = source };

        svc.Stage(cfg);
        File.WriteAllBytes(Path.Combine(source, "com.example_3.apk"), "PK\0\0"u8.ToArray());

        svc.Stage(cfg);

        File.ReadAllBytes(Path.Combine(target, "com.example_3.apk")).Should().Equal("PK\0\0"u8.ToArray());
    }

    [Fact]
    public void Stage_OwnsTheTargetDirectoryAndRemovesForeignFiles()
    {
        var (source, target) = WriteRepo();
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "left-over-by-somebody-else.txt"), "gone");
        var svc = Create(target);

        svc.Stage(new RepoConfig { RepoPath = source });

        File.Exists(Path.Combine(target, "left-over-by-somebody-else.txt"))
            .Should().BeFalse("the staged directory belongs to the staging service");
        File.Exists(Path.Combine(target, "index.xml")).Should().BeTrue();
    }

    [Fact]
    public void Stage_NoopsWhenUnconfiguredOrSourceIsTarget()
    {
        var (source, _) = WriteRepo();
        var cfg = new RepoConfig { RepoPath = source };

        Create(target: null).Stage(cfg);
        Directory.Exists(Path.Combine(source, "public")).Should().BeFalse();

        Create(source).Stage(cfg);
        File.Exists(Path.Combine(source, "config.yml")).Should().BeTrue("staging must never rewrite the fdroid output dir");
    }

    [Fact]
    public void Stage_RemovesDirectoriesThatEmptOut()
    {
        var (source, target) = WriteRepo();
        var svc = Create(target);
        var cfg = new RepoConfig { RepoPath = source };
        Directory.CreateDirectory(Path.Combine(source, "icons-160"));

        svc.Stage(cfg);

        Directory.CreateDirectory(Path.Combine(target, "icons-160"));
        svc.Stage(cfg);

        Directory.Exists(Path.Combine(target, "icons-160")).Should().BeFalse("an emptied icon bucket must not linger in the served dir");
    }

    [Fact]
    public void Stage_MissingSourceLeavesTargetUntouched()
    {
        var target = Path.Combine(_root, "public");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        var svc = Create(target);

        svc.Stage(new RepoConfig { RepoPath = Path.Combine(_root, "nope") });

        File.Exists(Path.Combine(target, "keep.txt")).Should().BeTrue();
    }

    [Fact]
    public void ResolvePublicDir_PrefersConfiguration_ThenEnvironment()
    {
        var env = new FakeEnvironmentService { Vars = { ["FDROID_REPO_PUBLIC_PATH"] = "/from/env" } };

        Create(TestConfig.Empty, env).ResolvePublicDir().Should().Be(Path.GetFullPath("/from/env"));

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FDROID_REPO_PUBLIC_PATH"] = "/from/config" })
            .Build();
        Create(configured, env).ResolvePublicDir().Should().Be(Path.GetFullPath("/from/config"));

        Create(TestConfig.Empty, new FakeEnvironmentService())
            .ResolvePublicDir()
            .Should().BeNull("staging stays off unless it is configured");
    }

    private RepoStageService Create(string? target)
    {
        var config = target is null
            ? TestConfig.Empty
            : new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FDROID_REPO_PUBLIC_PATH"] = target })
                .Build();

        return Create(config, new FakeEnvironmentService());
    }

    private static RepoStageService Create(IConfiguration config, FakeEnvironmentService env) =>
        new(TestIo.Real, config, env, TestVisibility.Real, NullLogger<RepoStageService>.Instance);

    private (string Source, string Target) WriteRepo()
    {
        var source = Path.Combine(_root, "repo");
        var target = Path.Combine(_root, "public");
        Directory.CreateDirectory(Path.Combine(source, "icons-320"));
        Directory.CreateDirectory(Path.Combine(source, "secrets"));

        File.WriteAllText(Path.Combine(source, "index.xml"), "<fdroid />");
        File.WriteAllBytes(Path.Combine(source, "com.example_3.apk"), [0x50, 0x4B, 0x03, 0x04]);
        File.WriteAllText(Path.Combine(source, "icons-320", "com.example.png"), "png");
        File.WriteAllText(Path.Combine(source, "config.yml"), "keystore: secret");
        File.WriteAllText(Path.Combine(source, "repo.p12"), "p12");
        File.WriteAllText(Path.Combine(source, "secrets", "app.jks"), "jks");
        File.WriteAllText(Path.Combine(source, ".hidden"), "dotfile");

        return (source, target);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
