using System.Text.Json.Serialization;

namespace FdroidMgr.Api.Models;

public record ReleaseDto
{
    public string PackageName { get; init; } = "";

    public int VersionCode { get; init; }

    public string VersionName { get; init; } = "";

    public string ApkFileName { get; init; } = "";

    public string Sha256 { get; init; } = "";

    public long SizeBytes { get; init; }

    public string? SignerSha256 { get; init; }

    public int? MinSdk { get; init; }

    public int? TargetSdk { get; init; }

    public string[] Permissions { get; init; } = [];

    [JsonIgnore]
    public Func<string?>? ChangelogLoader { get; init; }

    public string? Changelog => ChangelogLoader?.Invoke();
}
