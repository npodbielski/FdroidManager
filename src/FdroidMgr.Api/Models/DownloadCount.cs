namespace FdroidMgr.Api.Models;

public class DownloadCount
{
    public int Id { get; set; }

    public string DayUtc { get; set; } = "";

    public string? PackageName { get; set; }

    public int? VersionCode { get; set; }

    public int Count { get; set; }
}
