using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Abstractions;

public interface ISignInDiagnostics
{
    SignInDiagnostics.Failure? Last { get; }

    void Record(Exception? exception);

    void Clear();
}
