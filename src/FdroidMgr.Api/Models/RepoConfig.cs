namespace FdroidMgr.Api.Models;

public class RepoConfig
{
    public int Id { get; set; } = 1;

    public string RepoUrl { get; set; } = "http://localhost:5000/fdroid/repo";

    public string RepoName { get; set; } = "My F-Droid Repository";

    public string RepoDescription { get; set; } = "";

    public string RepoPath { get; set; } = "";

    public string KeyPath { get; set; } = "";

    public string? OAuthIssuer { get; set; }

    public string? OAuthAudience { get; set; }

    public string? RepoKeystorePassEnc { get; set; }

    public string? MasterKeySalt { get; set; }

    public string? RepoKeySha256 { get; set; }

    public string? BackupPath { get; set; }

    public DateTime? LastIndexBuiltUtc { get; set; }

    public string? LastIndexError { get; set; }
}
