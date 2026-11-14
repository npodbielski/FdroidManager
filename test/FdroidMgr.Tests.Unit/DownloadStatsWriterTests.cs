using FdroidMgr.Api;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace FdroidMgr.Tests.Unit;

public class DownloadStatsWriterTests
{
    [Fact]
    public async Task Writer_RecordsQueuedHits()
    {
        var seen = new List<(string Package, int Version)>();
        using var host = WriterHost(seen);

        host.Queue.TryEnqueue("com.example", 5).Should().BeTrue();

        await WaitUntil(() => seen.Count == 1);
        seen.Should().Equal(("com.example", 5));
    }

    [Fact]
    public async Task Writer_KeepsDraining_AfterARecordThrows()
    {
        var seen = new List<(string Package, int Version)>();
        using var host = WriterHost(seen, failFor: "com.bad");

        host.Queue.TryEnqueue("com.bad", 1).Should().BeTrue();
        host.Queue.TryEnqueue("com.good", 2).Should().BeTrue();

        await WaitUntil(() => seen.Contains(("com.good", 2)));
    }

    [Fact]
    public void Queue_DropsNothing_BelowCapacity()
    {
        IDownloadStatsQueue queue = new DownloadStatsQueue(NullLogger<DownloadStatsQueue>.Instance);

        for (var i = 1; i <= 512; i++)
        {
            queue.TryEnqueue("com.example", i).Should().BeTrue();
        }

        queue.Dropped.Should().Be(0);
        queue.Reader.Count.Should().Be(512);
    }

    private sealed class WriterHarness(DownloadStatsQueue queue, ServiceProvider provider) : IDisposable
    {
        public DownloadStatsQueue Queue { get; } = queue;

        public void Dispose() => provider.Dispose();
    }

    private static WriterHarness WriterHost(List<(string Package, int Version)> seen, string? failFor = null)
    {
        var queue = new DownloadStatsQueue(NullLogger<DownloadStatsQueue>.Instance);

        var stats = Substitute.For<IDownloadStatsService>();
        stats.RecordAsync(Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var package = call.ArgAt<string?>(0)!;
                var version = call.ArgAt<int?>(1)!.Value;
                if (failFor is not null && package == failFor)
                {
                    return Task.FromException(new InvalidOperationException("db is busy"));
                }

                lock (seen)
                {
                    seen.Add((package, version));
                }

                return Task.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDownloadStatsQueue>(queue);
        services.AddSingleton(stats);
        services.AddSingleton<DownloadStatsWriter>();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DownloadStatsWriter>().StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new WriterHarness(queue, provider);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("the writer never caught up");
    }
}
