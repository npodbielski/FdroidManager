using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component;

public class TestHostTests
{
    [Fact]
    public void Host_BootsWithItsOwnConfigFile_AndRemovesItsTempRoot()
    {
        var host = new TestHost(new TestHostOptions { DbName = "host.db", DirPrefix = "fdroidmgr-hosttest" });
        try
        {

            host.Factory.Services.GetRequiredService<IConfiguration>()["FDROID_CONFIG_FILE"]
                .Should().Be(host.ConfigFile);

            File.Exists(host.ConfigFile).Should().BeTrue();
            Path.GetDirectoryName(host.ConfigFile).Should().Be(host.Root);
            Directory.Exists(host.RepoDir).Should().BeTrue();
            Directory.Exists(host.KeyDir).Should().BeTrue();
            host.DbPath.Should().Be(Path.Combine(host.Root, "host.db"));
            host.Runner.Should().NotBeNull();

            using var other = new TestHost(new TestHostOptions { DbName = "host.db", DirPrefix = "fdroidmgr-hosttest" });
            other.Root.Should().NotBe(host.Root);
        }
        finally
        {
            host.Dispose();
        }

        Directory.Exists(host.Root).Should().BeFalse("the temp root is the host's to clean up");
    }
}
