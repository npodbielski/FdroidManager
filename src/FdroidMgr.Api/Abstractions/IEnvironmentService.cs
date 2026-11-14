namespace FdroidMgr.Api.Abstractions;

public interface IEnvironmentService
{
    string? GetEnvironmentVariable(string name);

    string GetCurrentDirectory();
}
