namespace FdroidMgr.Api.Models;

public record CreateApiKeyRequest
{
    public string Name { get; set; } = "";

    public string[]? Scopes { get; set; }

    public int? ExpiresDays { get; set; }
}
