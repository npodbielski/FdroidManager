using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class ReleaseServiceTests
{
    private readonly string _repoPath;

    private readonly string _keyPath;

    public ReleaseServiceTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "fdroidmgr-test-repo-" + Guid.NewGuid().ToString("N"));
        _keyPath = Path.Combine(Path.GetTempPath(), "fdroidmgr-test-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoPath);
        Directory.CreateDirectory(_keyPath);
    }

    [Fact]
    public async Task PublishAsync_RejectsUnknownApp()
    {
        var (_, svc, _, _, _, _) = CreateService(appExists: false);
        var act = async () => await svc.PublishAsync("org.example.test", ApkForm(), null, null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task PublishAsync_RejectsWrongPackage()
    {
        var (db, svc, _, _, _, _) = CreateService(package: "org.example.other");
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var act = async () => await svc.PublishAsync("org.example.test", ApkForm(), null, null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match app*");
    }

    [Fact]
    public async Task PublishAsync_DropsApkUnderUploadName_AndWritesChangelog_AndReindexes()
    {
        var (db, svc, repo, _, index, _) = CreateService();
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var result = await svc.PublishAsync("org.example.test", ApkForm(), "first changes", null, TestContext.Current.CancellationToken);

        result.VersionCode.Should().Be(1);
        result.ApkFileName.Should().Be("org.example.test_1.apk");
        result.Changelog.Should().Be("first changes");

        File.Exists(Path.Combine(_repoPath, "app.apk")).Should().BeTrue("APK should land under its upload name");
        File.Exists(Path.Combine(_repoPath, "org.example.test_1.apk")).Should().BeFalse("no canonical-name copy");

        await repo.Received(1).WriteChangelogAsync(Arg.Any<RepoConfig>(), "org.example.test", 1, "first changes", Arg.Any<CancellationToken>());

        await index.Received(1).GenerateStrictAsync(Arg.Any<RepoConfig>(), Arg.Any<bool>(), Arg.Any<IRepoLockLease>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_SecondRelease_RequiresHigherVersionCode()
    {

        var (db, svc, _, _, _, _) = CreateService(initialIndex: IndexWith("org.example.test", 2));
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var act = async () => await svc.PublishAsync("org.example.test", ApkForm(), null, null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not greater*");
    }

    [Fact]
    public async Task PublishAsync_DuplicateVersionCode_Rejected()
    {
        var (db, svc, _, _, _, _) = CreateService(versionCode: 2, initialIndex: IndexWith("org.example.test", 2));
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var act = async () => await svc.PublishAsync("org.example.test", ApkForm(), null, null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ReleaseConflictException>()
            .WithMessage("*already published*");
    }

    [Fact]
    public async Task PublishAsync_ReturnsVersionReadBackFromIndex()
    {
        var (db, svc, repo, _, _, _) = CreateService(versionCode: 2);
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        repo.ReadIndexAsync(Arg.Any<RepoConfig>(), Arg.Any<CancellationToken>())
            .Returns(null, IndexWith("org.example.test", 2) with { Timestamp = 999 });

        var result = await svc.PublishAsync("org.example.test", ApkForm(), null, null, TestContext.Current.CancellationToken);

        result.VersionCode.Should().Be(2);
        result.ApkFileName.Should().Be("org.example.test_2.apk");
        result.SignerSha256.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_ConcurrentSameVersionCode_ExactlyOneSucceeds()
    {
        var repoLock = new RepoLock();
        var published = new List<int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var insideGenerate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (dbA, svcA) = CreateService(repoLock: repoLock, published: published, generateGate: (insideGenerate, gate));
        var (dbB, svcB) = CreateService(repoLock: repoLock, published: published, generateGate: null);
        await TestDb.SeedKeyAsync(dbA, "org.example.test", SignerFp("org.example.test"));
        await TestDb.SeedKeyAsync(dbB, "org.example.test", SignerFp("org.example.test"));

        var ct = TestContext.Current.CancellationToken;
        var first = svcA.PublishAsync("org.example.test", ApkForm(), null, null, ct);
        await insideGenerate.Task;
        var second = svcB.PublishAsync("org.example.test", ApkForm(), null, null, ct);

        await Task.Delay(200, ct);
        second.IsCompleted.Should().BeFalse("the second publish must wait for the repo lock");

        gate.SetResult();

        var firstResult = await first;
        firstResult.VersionCode.Should().Be(1);

        var act = async () => await second;
        await act.Should().ThrowAsync<ReleaseConflictException>().WithMessage("*already published*");
        published.Should().Equal(1);
    }

    private (FdroidDb db, ReleaseService svc) CreateService(
        RepoLock repoLock,
        List<int> published,
        (TaskCompletionSource Inside, TaskCompletionSource Gate)? generateGate)
    {
        var db = TestDb.Create();
        TestDb.SeedRepoConfigAsync(db, _repoPath, _keyPath).GetAwaiter().GetResult();

        var inspector = Substitute.For<IApkInspector>();
        inspector.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ApkMetadata
            {
                PackageName = "org.example.test",
                VersionCode = 1,
                VersionName = "1.0.0",
                MinSdk = 21,
                TargetSdk = 34,
                Label = "Test App",
                Permissions = ["android.permission.INTERNET"],
                LaunchableActivity = "org.example.test.MainActivity",
                Sha256 = "ab".PadRight(64, 'c'),
            }));
        inspector.VerifySignature(Arg.Any<string>()).Returns(new ApkSignature(ApkSignatureStatus.Unsigned));

        var index = Substitute.ForPartsOf<RepoIndexService>(db, new FakeCommandRunner(), NullLogger<RepoIndexService>.Instance, TestIo.Real, repoLock, TestStages.Create());
        index.GenerateStrictAsync(Arg.Any<RepoConfig>(), Arg.Any<bool>(), Arg.Any<IRepoLockLease>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (generateGate is { } g)
                {
                    g.Inside.SetResult();
                    await g.Gate.Task;
                }

                lock (published)
                {
                    published.Add(1);
                }
            });

        var repo = Substitute.For<FdroidRepoService>(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot());
        repo.ReadIndexAsync(Arg.Any<RepoConfig>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lock (published)
                {
                    return Task.FromResult(published.Count == 0 ? null : IndexWith("org.example.test", published.Max()));
                }
            });

        var keySvc = Substitute.ForPartsOf<KeyService>(db, null!, TestIo.Real, new FakeEnvironmentService());
        keySvc.GetAvailableKeyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var pkg = call.ArgAt<string>(0);
                var key = db.Keys.FirstOrDefault(k => k.PackageName == pkg)
                          ?? throw new InvalidOperationException("no key in test db");
                return Task.FromResult((key, "test-password"));
            });
        keySvc.SignApkAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.Copy(call.ArgAt<string>(1), call.ArgAt<string>(2), overwrite: true);
                return Task.CompletedTask;
            });

        var metadata = Substitute.ForPartsOf<FdroidMetadataService>(db, NullLogger<FdroidMetadataService>.Instance, TestIo.Real, new FakeEnvironmentService());
        metadata.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var svc = new ReleaseService(db, inspector, keySvc, index, metadata, repo, NullLogger<ReleaseService>.Instance, TestIo.Real, repoLock);
        return (db, svc);
    }

    private (FdroidDb db, ReleaseService svc, FdroidRepoService repo, FdroidMetadataService metadata, RepoIndexService index, KeyService keys) CreateService(
        string package = "org.example.test",
        int versionCode = 1,
        string versionName = "1.0.0",
        FdroidIndex? initialIndex = null,
        bool appExists = true,
        IApkInspector? apk = null,
        ILogger<ReleaseService>? logger = null)
    {
        var db = TestDb.Create();
        TestDb.SeedRepoConfigAsync(db, _repoPath, _keyPath).GetAwaiter().GetResult();

        var inspector = apk ?? Substitute.For<IApkInspector>();
        if (apk is null)
        {
            inspector.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(new ApkMetadata
                {
                    PackageName = package,
                    VersionCode = versionCode,
                    VersionName = versionName,
                    MinSdk = 21,
                    TargetSdk = 34,
                    Label = "Test App",
                    Permissions = ["android.permission.INTERNET"],
                    LaunchableActivity = package + ".MainActivity",
                    Sha256 = "ab".PadRight(64, 'c'),
                }));

            inspector.VerifySignature(Arg.Any<string>()).Returns(new ApkSignature(ApkSignatureStatus.Unsigned));
        }

        var index = Substitute.ForPartsOf<RepoIndexService>(db, new FakeCommandRunner(), NullLogger<RepoIndexService>.Instance, TestIo.Real, new RepoLock(), TestStages.Create());
        index.GenerateStrictAsync(Arg.Any<RepoConfig>(), Arg.Any<bool>(), Arg.Any<IRepoLockLease>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var keySvc = Substitute.ForPartsOf<KeyService>(db, null!, TestIo.Real, new FakeEnvironmentService());
        keySvc.GetAvailableKeyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var pkg = call.ArgAt<string>(0);
                var key = db.Keys.FirstOrDefault(k => k.PackageName == pkg)
                          ?? throw new InvalidOperationException("no key in test db");
                return Task.FromResult((key, "test-password"));
            });
        keySvc.SignApkAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.Copy(call.ArgAt<string>(1), call.ArgAt<string>(2), overwrite: true);
                return Task.CompletedTask;
            });

        var metadata = Substitute.ForPartsOf<FdroidMetadataService>(db, NullLogger<FdroidMetadataService>.Instance, TestIo.Real, new FakeEnvironmentService());
        metadata.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(appExists);

        var repo = Substitute.For<FdroidRepoService>(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot());
        repo.ReadIndexAsync(Arg.Any<RepoConfig>(), Arg.Any<CancellationToken>()).Returns(initialIndex);

        var svc = new ReleaseService(db, inspector, keySvc, index, metadata, repo,
            logger ?? NullLogger<ReleaseService>.Instance, TestIo.Real, new RepoLock());
        return (db, svc, repo, metadata, index, keySvc);
    }

    [Fact]
    public async Task PublishAsync_ReplacesUnsafeCharactersInUploadName()
    {
        var (db, svc, _, _, _, _) = CreateService();
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var result = await svc.PublishAsync(
            "org.example.test", ApkForm(fileName: "../../weird name[1].apk"), null, null,
            TestContext.Current.CancellationToken);

        // The index reports the canonical name; the upload name only decides the
        // file on disk, and must stay filename-safe.
        result.ApkFileName.Should().Be("org.example.test_1.apk");
        File.Exists(Path.Combine(_repoPath, "weird_name_1_.apk")).Should().BeTrue();
        Directory.EnumerateFiles(_repoPath, "*weird*")
            .Should().OnlyContain(x => Path.GetFileName(x) == "weird_name_1_.apk");
    }

    [Fact]
    public async Task PublishAsync_SignatureToolFails_AbortsWithoutSigning()
    {
        // Without a working verifier the APK's signature is unknown, and unknown
        // is not the same as unsigned: signing here could re-sign tampered code.
        var throwing = Substitute.For<IApkInspector>();
        throwing.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApkMetadata
            {
                PackageName = "org.example.test",
                VersionCode = 1,
                VersionName = "1.0.0",
                Sha256 = "ab".PadRight(64, 'c'),
            });
        throwing.When(x => x.VerifySignature(Arg.Any<string>())).Throw(new IOException("apksigner missing"));

        var (db, svc, _, _, _, _) = CreateService(apk: throwing);
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var act = async () => await svc.PublishAsync("org.example.test", ApkForm(), null, null,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task PublishAsync_BrokenSignature_IsRefusedAndNotResigned()
    {
        var inspector = Substitute.For<IApkInspector>();
        inspector.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApkMetadata
            {
                PackageName = "org.example.test",
                VersionCode = 1,
                VersionName = "1.0.0",
                Sha256 = "ab".PadRight(64, 'c'),
            });
        inspector.VerifySignature(Arg.Any<string>())
            .Returns(new ApkSignature(ApkSignatureStatus.Broken, Detail: "DOES NOT VERIFY"));

        var (db, svc, _, _, index, keys) = CreateService(apk: inspector);
        await TestDb.SeedKeyAsync(db, "org.example.test", SignerFp("org.example.test"));

        var act = async () => await svc.PublishAsync("org.example.test", ApkForm(), null, null,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not verify*DOES NOT VERIFY*");
        await keys.DidNotReceiveWithAnyArgs()
            .SignApkAsync(default!, default!, default!, default, default);
        await index.DidNotReceiveWithAnyArgs()
            .GenerateStrictAsync(default!, default, default!, default);
        File.Exists(Path.Combine(_repoPath, "org.example.test_1.apk")).Should().BeFalse();
    }

    private static IFormFile ApkForm(byte[]? bytes = null, string fileName = "app.apk")
    {
        bytes ??= [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "apk", fileName);
    }

    private static FdroidIndex IndexWith(string package, int versionCode)
        => new("Test Repo", "http://localhost/fdroid/repo", 0,
            [new FdroidApp(package, "Test App", "s", "d", "MIT", null, null, null, null, null,
                [new FdroidVersion(versionCode, "1.0.0", $"{package}_{versionCode}.apk",
                    "ab".PadRight(64, 'c'), 1234, null, 21, 34, [])])]);

    private static string SignerFp(string package)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(package))).ToLowerInvariant();
}
