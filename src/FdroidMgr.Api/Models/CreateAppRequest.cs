namespace FdroidMgr.Api.Models;

public record CreateAppRequest
{
    public string PackageName { get; set; } = "";

    public string Name { get; set; } = "";

    public string Summary { get; set; } = "";

    public string Description { get; set; } = "";

    public string License { get; set; } = "MIT";

    public string? Website { get; set; }

    public string? SourceCode { get; set; }

    public string? IssueTracker { get; set; }

    public bool GenerateKey { get; set; } = true;
}
