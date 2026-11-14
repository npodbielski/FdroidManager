using FluentAssertions;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Tests.Unit.Services;

public class SignInDiagnosticsTests
{
    [Fact]
    public void EmptyDiagnostics_HaveNoError()
    {
        var d = new SignInDiagnostics();

        d.Last.Should().BeNull();
    }

    [Fact]
    public void Record_SingleException_RecordsItsMessage()
    {
        var d = new SignInDiagnostics();

        d.Record(new InvalidOperationException("IDX20803: Unable to obtain configuration from: 'https://idp/.well-known'."));

        d.Last!.Message.Should().Be("IDX20803: Unable to obtain configuration from: 'https://idp/.well-known'.");
        d.Last!.Utc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Record_WrappedException_AppendsTheDeepestReason()
    {
        var d = new SignInDiagnostics();
        var ex = new InvalidOperationException("IDX20803: Unable to obtain configuration",
            new HttpRequestException("Connection refused (idp:443)"));

        d.Record(ex);

        d.Last!.Message.Should()
            .Be("IDX20803: Unable to obtain configuration — Connection refused (idp:443)");
    }

    [Fact]
    public void Record_ReasonRepeatedByOuter_Message_RecordsItOnce()
    {
        var d = new SignInDiagnostics();
        var inner = new Exception("Connection refused");
        var ex = new InvalidOperationException("metadata fetch failed: Connection refused", inner);

        d.Record(ex);

        d.Last!.Message.Should().Be("metadata fetch failed: Connection refused");
    }

    [Fact]
    public void Record_RunawayMessage_IsCappedWithEllipsis()
    {
        var d = new SignInDiagnostics();

        d.Record(new string('x', 5_000));

        d.Last!.Message.Length.Should().Be(401);
        d.Last!.Message.Should().EndWith("…");
    }

    [Fact]
    public void Record_NothingSaying_KeepsThePreviousCause()
    {
        var d = new SignInDiagnostics();
        d.Record("first cause");

        d.Record((string?)null);
        d.Record("   ");
        d.Record((Exception?)null);

        d.Last!.Message.Should().Be("first cause");
    }

    [Fact]
    public void Clear_ForgetsTheCause()
    {
        var d = new SignInDiagnostics();
        d.Record("boom");

        d.Clear();

        d.Last.Should().BeNull();
    }
}
