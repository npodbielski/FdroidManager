
namespace FdroidMgr.Tests.Component.Fixtures;

public sealed class ApiFixture : IDisposable
{
    private readonly TestHost _host;

    public HttpClient Client { get; }

    public ApiFixture()
    {
        _host = new TestHost(new TestHostOptions
        {
            DbName = "int.db",

            DirPrefix = "fdroidmgr-int",
        });

        File.WriteAllText(Path.Combine(_host.RepoDir, "index.xml"),
            "<fdroid><repo name=\"Test Repo\" /></fdroid>");

        Client = _host.Client(allowAutoRedirect: true);
    }

    public HttpClient Authenticated() => _host.Client("admin", allowAutoRedirect: true);

    public void Dispose()
    {
        Client.Dispose();
        _host.Dispose();
    }
}
