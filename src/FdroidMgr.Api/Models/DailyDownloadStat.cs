
namespace FdroidMgr.Api.Models;

public record DailyDownloadStat
{
    public string Day { get; init; } = "";

    public long Downloads { get; init; }
}
