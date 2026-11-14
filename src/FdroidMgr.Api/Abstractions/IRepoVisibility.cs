namespace FdroidMgr.Api.Abstractions;

public interface IRepoVisibility
{
    string RelativeTo(string root, string full);

    bool IsDeniedPath(string relativePath);

    bool IsWithin(string candidate, string root);
}
