using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public partial class OAuthConfigService(IConfigurationRoot config, ILogger<OAuthConfigService> log, IOService io)
    : IOAuthConfigService
{

    private bool _configuredThisBoot;
    private string? _configuredAuthority;

    private string ConfigPath { get; } = OidcAuthExtensions.ResolveConfigFile(config);


    public bool IsConfigured()
        => _configuredThisBoot || !string.IsNullOrWhiteSpace(config["Auth:OAuthIssuer"]);

    public OAuthConfigStatus GetStatus()
    {

        if (!IsConfigured())
        {
            return new OAuthConfigStatus { Configured = false };
        }

        var scopes = ScopeSplit(config["Auth:OAuthScopes"] ?? "");
        return new OAuthConfigStatus
        {
            Configured = true,
            RestartRequired = false,
            Authority = ResolveAuthority(),
            ClientId = config["Auth:OAuthClientId"],
            Scopes = scopes.Length == 0 ? null : scopes,
            ConfiguredAt = io.FileExists(ConfigPath) ? io.GetLastWriteTimeUtc(ConfigPath) : null,
        };
    }

    public static void Validate(OAuthConfigRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Authority))
        {
            throw new ArgumentException("authority is required");
        }

        if (!Uri.TryCreate(req.Authority, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("authority must be a valid http(s) URL");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !req.AllowInsecure)
        {
            throw new ArgumentException("authority must be an https URL (set allowInsecure for LAN dev)");
        }

        if (string.IsNullOrWhiteSpace(req.ClientId))
        {
            throw new ArgumentException("clientId is required");
        }

        if (req.Scopes is null)
        {
            return;
        }

        foreach (var s in req.Scopes)
        {
            if (string.IsNullOrWhiteSpace(s) || !Regex.IsMatch(s, @"^[A-Za-z0-9_.\-]+$"))
            {
                throw new ArgumentException($"invalid scope: {s}");
            }
        }
    }

    public async Task<OAuthConfigStatus> ConfigureAsync(OAuthConfigRequest req, CancellationToken ct = default)
    {
        if (IsConfigured())
        {
            throw new OAuthAlreadyConfiguredException(ResolveAuthority() ?? "");
        }

        Validate(req);

        var scopes = NormalizeScopes(req.Scopes);
        var content = await BuildConfigAsync(req, scopes, ct);
        WriteAtomic(ConfigPath, content);

        config.Reload();
        _configuredThisBoot = true;
        _configuredAuthority = req.Authority;

        Log.OAuthConfigured(log,
            Uri.EscapeDataString(req.Authority!));

        return new OAuthConfigStatus
        {
            Configured = true,
            Authority = req.Authority,
            ClientId = req.ClientId,
            Scopes = scopes,
            RestartRequired = false,
            ConfiguredAt = DateTime.UtcNow,
        };
    }

    private string? ResolveAuthority()
        => config["Auth:OAuthIssuer"] ?? _configuredAuthority;

    internal static string[] NormalizeScopes(string[]? scopes)
    {
        var set = new List<string> { "openid", "profile", "email" };
        if (scopes is null)
        {
            return [.. set];
        }

        foreach (var s in scopes)
        {
            if (!string.IsNullOrWhiteSpace(s) && !set.Contains(s))
            {
                set.Add(s);
            }
        }

        return [.. set];
    }

    internal static string[] ScopeSplit(string raw)
        => raw.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

    private async Task<string> BuildConfigAsync(OAuthConfigRequest req, string[] scopes, CancellationToken ct)
    {
        JsonObject root;
        if (io.FileExists(ConfigPath))
        {
            try
            {
                root = JsonNode.Parse(await io.ReadAllTextAsync(ConfigPath, ct))?.AsObject() ?? new JsonObject();
            }
            catch (JsonException)
            {
                Log.ConfigFileNotJson(log, ConfigPath);
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        if (root["Auth"] is not JsonObject auth)
        {
            auth = new JsonObject();
            root["Auth"] = auth;
        }

        auth["OAuthIssuer"] = req.Authority;
        auth["OAuthClientId"] = req.ClientId;
        auth["OAuthScopes"] = string.Join(' ', scopes);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private void WriteAtomic(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        io.CreateDirectory(dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        io.WriteAllText(temp, content);
        if (!OperatingSystem.IsWindows())
        {
            io.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        io.MoveFile(temp, full, overwrite: true);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 29, Level = LogLevel.Information,
            Message = "OAuth configured (issuer {Issuer}) — effective immediately, no restart required")]
        public static partial void OAuthConfigured(ILogger logger, string issuer);

        [LoggerMessage(EventId = 30, Level = LogLevel.Warning,
            Message = "config file {Path} was not valid JSON — rebuilding the Auth section only")]
        public static partial void ConfigFileNotJson(ILogger logger, string path);
    }
}
