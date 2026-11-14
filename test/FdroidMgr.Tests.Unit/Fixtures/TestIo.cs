using FdroidMgr.Api.Services;
using FdroidMgr.Api.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FdroidMgr.Tests.Unit.Fixtures;

public static class TestIo
{
    public static IOService Real { get; } = new RealIoService();
}

public static class TestConfig
{
    public static IConfiguration Empty { get; } = new ConfigurationBuilder().Build();
}

public static class TestStages
{
    public static RepoStageService Create(IOService? io = null)
        => new(io ?? TestIo.Real, TestConfig.Empty, new FakeEnvironmentService(), TestVisibility.Real, NullLogger<RepoStageService>.Instance);
}

public static class TestVisibility
{
    public static IRepoVisibility Real { get; } = new RepoVisibility();
}

public sealed class FakeEnvironmentService : IEnvironmentService
{
    public Dictionary<string, string?> Vars { get; } = new(StringComparer.Ordinal);

    public string? GetEnvironmentVariable(string name)
        => Vars.TryGetValue(name, out var v) ? v : Environment.GetEnvironmentVariable(name);

    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
}
