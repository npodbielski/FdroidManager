using System.Text.Json.Serialization;

namespace FdroidMgr.Api.Models;

public class ApiKeyListItem
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string KeyPrefix { get; set; } = "";

    [JsonConverter(typeof(CommaSeparatedStringArrayConverter))]
    public string Scopes { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public DateTime? LastUsedUtc { get; set; }

    public DateTime? ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }
}
