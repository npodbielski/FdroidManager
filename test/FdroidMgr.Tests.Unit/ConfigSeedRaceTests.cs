using System.Collections.Concurrent;
using FdroidMgr.Api;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace FdroidMgr.Tests.Unit;

public class ConfigSeedRaceTests
{
    [Fact]
    public void ConcurrentSeed_NeverExposesAFileWithoutJson()
    {
        var errors = new ConcurrentQueue<Exception>();

        for (var round = 0; round < 30; round++)
        {
            var root = Path.Combine(Path.GetTempPath(), "fdroidmgr-seed-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "appsettings.json");
            Directory.CreateDirectory(root);

            try
            {
                var barrier = new Barrier(8);
                var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
                {
                    barrier.SignalAndWait();
                    try
                    {
                        OidcAuthExtensions.SeedConfigFile(path);

                        new ConfigurationBuilder().AddJsonFile(path, optional: true).Build();
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                })).ToList();

                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                File.Exists(path).Should().BeTrue($"round {round}: one seeder must have created the file");
                File.ReadAllText(path).Trim().Should().Be("{}", $"round {round}: the file must only ever hold the seed object");
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch (IOException) { }
            }
        }

        errors.Should().BeEmpty();
    }
}
