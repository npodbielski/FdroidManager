namespace FdroidMgr.Api.Models;

public class AppKey
{
    public int Id { get; set; }

    public string PackageName { get; set; } = "";

    public string Alias { get; set; } = "release";

    public string? Branch { get; set; }

    public string StorePath { get; set; } = "";

    public string FingerprintSha256 { get; set; } = "";

    public string PasswordEncrypted { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
