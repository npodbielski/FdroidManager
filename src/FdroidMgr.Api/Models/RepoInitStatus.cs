
namespace FdroidMgr.Api.Models;

public record RepoInitStatus
{
    public bool Initialized { get; set; }

    public string RepoPath { get; set; } = "";

    public string? Name { get; set; }

    public string SuggestedRepoUrl { get; set; } = "";
}
