
namespace FdroidMgr.Api.Models;

public record AppMetadataFields
{
    public string Name { get; init; } = "";

    public string Summary { get; init; } = "";

    public string Description { get; init; } = "";

    public string License { get; init; } = "Unknown";

    public string? Website { get; init; }

    public string? SourceCode { get; init; }

    public string? IssueTracker { get; init; }
}
