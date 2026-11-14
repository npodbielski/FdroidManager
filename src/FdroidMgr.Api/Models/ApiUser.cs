namespace FdroidMgr.Api.Models;

public class ApiUser
{
    public int Id { get; set; }

    public string Subject { get; set; } = "";

    public string Username { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<ApiKey> ApiKeys { get; set; } = [];
}
