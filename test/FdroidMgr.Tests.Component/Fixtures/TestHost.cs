using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;


namespace FdroidMgr.Tests.Component.Fixtures;

public sealed record TestHostOptions
{

    public string DbName { get; init; } = "test.db";

    public string MasterKey { get; init; } = TestHost.DefaultMasterKey;

    public bool TestAuth { get; init; } = true;

    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

    public Action<FakeCommandRunner>? Runner { get; init; }

    public Action<IServiceCollection>? Services { get; init; }

    public string DirPrefix { get; init; } = "fdroidmgr-host";

    public string? Root { get; init; }
}

public sealed class TestHost : IDisposable
{

    public const string DefaultMasterKey = "fdroidmgr-test-master-key-0123456789abcdef";

    public const string ConfigFileName = "test-appsettings.json";

    public WebApplicationFactory<Program> Factory { get; }

    public FakeCommandRunner Runner { get; }

    public string Root { get; }

    public string RepoDir { get; }

    public string KeyDir { get; }

    public string DbPath { get; }

    public string ConfigFile { get; }

    public TestHost(TestHostOptions? options = null)
    {
        var o = options ?? new TestHostOptions();

        Root = o.Root ?? Path.Combine(Path.GetTempPath(), $"{o.DirPrefix}-{Guid.NewGuid():N}");
        RepoDir = Path.Combine(Root, "repo");
        KeyDir = Path.Combine(Root, "keys");
        DbPath = Path.Combine(Root, o.DbName);
        Directory.CreateDirectory(RepoDir);
        Directory.CreateDirectory(KeyDir);

        ConfigFile = Path.Combine(Root, ConfigFileName);
        File.WriteAllText(ConfigFile, "{}\n");

        Runner = new FakeCommandRunner();
        o.Runner?.Invoke(Runner);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
                builder.UseSetting("FDROID_REPO_PATH", RepoDir);
                builder.UseSetting("FDROID_KEY_PATH", KeyDir);
                builder.UseSetting("FDROID_MASTER_KEY", o.MasterKey);
                builder.UseSetting("FDROID_CONFIG_FILE", ConfigFile);

                if (o.Settings is not null)
                {
                    foreach (var (key, value) in o.Settings)
                    {
                        builder.UseSetting(key, value);
                    }
                }

                builder.ConfigureServices(services =>
                {
                    if (o.TestAuth)
                    {
                        TestAuth.ConfigureServices(services);
                    }

                    services.RemoveAll<IRepoCommandRunner>();
                    services.AddSingleton<IRepoCommandRunner>(Runner);
                    o.Services?.Invoke(services);
                });
            });
    }

    public void Start() => _ = Client();

    public HttpClient Client(string? signedInAs = null, bool allowAutoRedirect = false, bool handleCookies = true)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = handleCookies,
        });

        if (signedInAs is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuth.Header, signedInAs);
        }

        return client;
    }

    public void Dispose()
    {
        Factory.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {

        }
    }
}
