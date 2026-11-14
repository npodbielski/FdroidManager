using System.Text.RegularExpressions;
using FluentAssertions;

namespace FdroidMgr.Tests.Component;

public class TestConfigIsolationTests
{
    private static readonly Regex Factory = new(@"new\s+WebApplicationFactory<Program>\s*\(", RegexOptions.Compiled);

    [Fact]
    public void OnlyTestHost_ConstructsABootedApiHost()
    {
        var sources = FindTestSourceDirectory();
        if (sources is null)
        {

            return;
        }

        var offenders = Directory
            .EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "TestHost.cs", StringComparison.Ordinal))
            .Where(file => Factory.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .ToList();

        offenders.Should().BeEmpty(
            "hosts are built by TestHost alone; a local WebApplicationFactory<Program> skips " +
            "the per-host config file (races /config/appsettings.json when running as root) and " +
            "repeats the db/repo/key/master-key wiring — use new TestHost(new TestHostOptions {{ ... }})");
    }

    private static string? FindTestSourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "test", "FdroidMgr.Tests.Component");
            if (File.Exists(Path.Combine(candidate, "Fixtures", "TestHost.cs")))
            {
                return candidate;
            }
        }

        return null;
    }
}
