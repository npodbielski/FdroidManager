namespace FdroidMgr.Api.Abstractions;

public interface IPasswordProtector
{
    string Protect(string plaintext);

    string Unprotect(string blob);

    bool IsLegacy(string blob);
}
