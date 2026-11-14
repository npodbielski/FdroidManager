using System.Text.Json;
using System.Text.Json.Serialization;

namespace FdroidMgr.Api.Services;

internal sealed record IndexFile
{
    [JsonPropertyName("repo")] public IndexRepo? Repo { get; init; }

    [JsonPropertyName("apps")] public JsonElement Apps { get; init; }

    [JsonPropertyName("packages")] public Dictionary<string, List<IndexVersion>>? Packages { get; init; }
}

internal sealed record IndexRepo
{
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("address")] public string? Address { get; init; }

    [JsonPropertyName("url")] public string? Url { get; init; }

    [JsonPropertyName("timestamp")] public long Timestamp { get; init; }
}

internal sealed record IndexApp
{
    [JsonPropertyName("packageName")] public string? PackageName { get; init; }

    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("summary")] public string? Summary { get; init; }

    [JsonPropertyName("description")] public string? Description { get; init; }

    [JsonPropertyName("license")] public string? License { get; init; }

    [JsonPropertyName("webSite")] public string? WebSite { get; init; }

    [JsonPropertyName("web")] public string? Web { get; init; }

    [JsonPropertyName("sourceCode")] public string? SourceCode { get; init; }

    [JsonPropertyName("source")] public string? Source { get; init; }

    [JsonPropertyName("issueTracker")] public string? IssueTracker { get; init; }

    [JsonPropertyName("tracker")] public string? Tracker { get; init; }

    [JsonPropertyName("icon")] public string? Icon { get; init; }

    [JsonPropertyName("localized")] public Dictionary<string, Dictionary<string, JsonElement>>? Localized { get; init; }

    [JsonPropertyName("versions")] public Dictionary<string, LegacyVersion>? Versions { get; init; }
}

internal sealed record IndexVersion
{
    [JsonPropertyName("versionCode")] public int VersionCode { get; init; }

    [JsonPropertyName("versionName")] public string? VersionName { get; init; }

    [JsonPropertyName("apkName")] public string? ApkName { get; init; }

    [JsonPropertyName("hash")] public string? Hash { get; init; }

    [JsonPropertyName("size")] public long Size { get; init; }

    [JsonPropertyName("signer")] public JsonElement Signer { get; init; }

    [JsonPropertyName("minSdkVersion")] public int? MinSdkVersion { get; init; }

    [JsonPropertyName("targetSdkVersion")] public int? TargetSdkVersion { get; init; }

    [JsonPropertyName("uses-permission")] public List<JsonElement>? UsesPermission { get; init; }

    [JsonPropertyName("usesPermission")] public List<JsonElement>? UsesPermissionCamel { get; init; }
}

internal sealed record LegacyVersion
{
    [JsonPropertyName("versionCode")] public int VersionCode { get; init; }

    [JsonPropertyName("versionName")] public string? VersionName { get; init; }

    [JsonPropertyName("file")] public LegacyFile? File { get; init; }

    [JsonPropertyName("manifest")] public LegacyManifest? Manifest { get; init; }
}

internal sealed record LegacyFile
{
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("size")] public long Size { get; init; }

    [JsonPropertyName("hash")] public LegacyHash? Hash { get; init; }
}

internal sealed record LegacyHash
{
    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
}

internal sealed record LegacyManifest
{
    [JsonPropertyName("usesSdk")] public LegacySdk? UsesSdk { get; init; }

    [JsonPropertyName("signer")] public LegacySigner? Signer { get; init; }

    [JsonPropertyName("uses-permission")] public List<JsonElement>? UsesPermission { get; init; }

    [JsonPropertyName("usesPermission")] public List<JsonElement>? UsesPermissionCamel { get; init; }
}

internal sealed record LegacySdk
{
    [JsonPropertyName("minSdkVersion")] public int? MinSdkVersion { get; init; }

    [JsonPropertyName("targetSdkVersion")] public int? TargetSdkVersion { get; init; }
}

internal sealed record LegacySigner
{
    [JsonPropertyName("sha256")] public List<string>? Sha256 { get; init; }
}

internal sealed record IndexV2File
{
    [JsonPropertyName("packages")] public Dictionary<string, IndexV2Package>? Packages { get; init; }
}

internal sealed record IndexV2Package
{
    [JsonPropertyName("metadata")] public IndexV2Metadata? Metadata { get; init; }

    [JsonPropertyName("icon")] public Dictionary<string, IndexV2Icon>? Icon { get; init; }
}

internal sealed record IndexV2Metadata
{
    [JsonPropertyName("icon")] public Dictionary<string, IndexV2Icon>? Icon { get; init; }
}

internal sealed record IndexV2Icon
{
    [JsonPropertyName("name")] public string? Name { get; init; }
}
