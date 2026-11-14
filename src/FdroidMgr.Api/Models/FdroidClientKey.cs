namespace FdroidMgr.Api.Models;

public class FdroidClientKey
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public ApiUser? Creator { get; set; }

    public string KeyHash { get; set; } = "";

    public string KeyPrefix { get; set; } = "";

    public string SecretCipher { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedUtc { get; set; }

    public DateTime? LastRevealedUtc { get; set; }

    public DateTime? RegeneratedUtc { get; set; }
}
