using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace FdroidMgr.Api.Services;

public class ScopeClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
            || string.Equals(identity.AuthenticationType, ApiKeyAuthenticationHandler.SchemeName,
                StringComparison.Ordinal))
        {
            return Task.FromResult(principal);
        }

        foreach (var scope in Scopes.All)
        {
            if (!identity.HasClaim(Scopes.ClaimType, scope))
            {
                identity.AddClaim(new Claim(Scopes.ClaimType, scope));
            }
        }

        return Task.FromResult(principal);
    }
}
