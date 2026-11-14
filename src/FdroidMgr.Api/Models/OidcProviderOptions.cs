namespace FdroidMgr.Api.Models;

public sealed class OidcProviderOptions
{
    public string? Issuer { get; set; }

    public string? ClientId { get; set; }

    public string Scopes { get; set; } = "";
}
