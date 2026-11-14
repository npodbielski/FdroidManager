using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FdroidMgr.Tests.Unit.Services;

public class DownloadStatsQueueTests
{
    [Fact]
    public async Task TryEnqueue_PassesHitsToTheReaderInOrder()
    {
        var queue = new DownloadStatsQueue(NullLogger<DownloadStatsQueue>.Instance);

        queue.TryEnqueue("com.example", 3).Should().BeTrue();
        queue.TryEnqueue("com.other", 1).Should().BeTrue();

        var first = await queue.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var second = await queue.Reader.ReadAsync(TestContext.Current.CancellationToken);

        first.Should().Be(("com.example", 3));
        second.Should().Be(("com.other", 1));
        queue.Dropped.Should().Be(0);
    }

    [Fact]
    public async Task TryEnqueue_CountsDropsOnceTheBufferIsFull()
    {
        var logged = new List<string>();
        var logger = Substitute.For<ILogger<DownloadStatsQueue>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        logger.When(x => x.Log(
                Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(),
                Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>()))
            .Do(call => logged.Add(call.ArgAt<object>(2).ToString()!));

        var queue = new DownloadStatsQueue(logger);
        var accepted = 0;
        while (queue.TryEnqueue("com.example", accepted + 1))
        {
            accepted++;
        }

        queue.Dropped.Should().Be(1);
        accepted.Should().BeGreaterThan(0, "the buffer holds a bounded number of hits");
        logged.Should().ContainSingle(x => x.Contains("download stats queue is full"));

        var hit = await queue.Reader.ReadAsync(TestContext.Current.CancellationToken);
        hit.Should().Be(("com.example", 1));
    }
}
