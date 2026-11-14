using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class EnvironmentService : IEnvironmentService
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
}
