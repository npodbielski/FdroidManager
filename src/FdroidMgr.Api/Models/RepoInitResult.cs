
namespace FdroidMgr.Api.Models;

public record RepoInitResult
{
    public string RepoUrl { get; set; } = "";

    public string RepoName { get; set; } = "";

    public string RepoPath { get; set; } = "";

    public string KeyAlias { get; set; } = "";

    public string RepoKeySha256 { get; set; } = "";

    public string? BackupPath { get; set; }
}
