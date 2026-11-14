namespace FdroidMgr.Api.Models;

public class OAuthConfigRequest
{

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string[]? Scopes { get; set; }

    public bool AllowInsecure { get; set; }
}

public class OAuthConfigStatus
{
    public bool Configured { get; set; }

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string[]? Scopes { get; set; }

    public bool RestartRequired { get; set; }

    public DateTime? ConfiguredAt { get; set; }

    public string? LastSignInError { get; set; }

    public DateTime? LastSignInErrorUtc { get; set; }
}

public class OAuthAlreadyConfiguredException(string authority) : Exception("oauth already configured")
{
    public string Authority { get; } = authority;
}
