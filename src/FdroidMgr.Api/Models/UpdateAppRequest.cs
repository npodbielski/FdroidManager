namespace FdroidMgr.Api.Models;

public record UpdateAppRequest
{
    public string? Name { get; set; }

    public string? Summary { get; set; }

    public string? Description { get; set; }

    public string? License { get; set; }

    public string? Website { get; set; }

    public string? SourceCode { get; set; }

    public string? IssueTracker { get; set; }
}
