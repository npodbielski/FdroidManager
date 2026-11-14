
namespace FdroidMgr.Api.Models;

public record RepoInitRequest
{
    public string RepoName { get; set; } = "";

    public string RepoDescription { get; set; } = "";

    public string? KeyAlias { get; set; }

    public string? KeystorePath { get; set; }

    public bool Force { get; set; }
}
