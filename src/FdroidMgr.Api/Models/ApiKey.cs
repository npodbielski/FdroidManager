namespace FdroidMgr.Api.Models;

public class ApiKey
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public ApiUser? User { get; set; }

    public string Name { get; set; } = "";

    public string KeyHash { get; set; } = "";

    public string KeyPrefix { get; set; } = "";

    public string Scopes { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedUtc { get; set; }

    public DateTime? ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }
}
