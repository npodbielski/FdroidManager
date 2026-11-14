using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class SignInDiagnostics : ISignInDiagnostics
{
    private const int MaxLength = 400;

    private Failure? _last;

    public Failure? Last => Volatile.Read(ref _last);

    public void Record(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        Record(Describe(exception));
    }

    public void Record(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var text = message.Trim();
        if (text.Length > MaxLength)
        {
            text = text[..MaxLength].TrimEnd() + "…";
        }

        Volatile.Write(ref _last, new Failure(text, DateTime.UtcNow));
    }

    public void Clear() => Volatile.Write(ref _last, null);

    private static string Describe(Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var outer = ex.Message.Trim();
        var inner = root.Message.Trim();
        return inner.Length == 0 || inner == outer || outer.Contains(inner, StringComparison.Ordinal)
            ? outer
            : $"{outer} — {inner}";
    }

    public sealed record Failure(string Message, DateTime Utc);
}
