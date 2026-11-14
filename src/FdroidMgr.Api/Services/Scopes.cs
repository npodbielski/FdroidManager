namespace FdroidMgr.Api.Services;

/// <summary>
/// The one source of truth for API-key scopes: the claim type they travel in,
/// the set of valid values, and how a requested set expands. Authorization
/// policies, request validation and key issuance all read it, so a scope can
/// never exist in one of them without existing in the others.
/// </summary>
public static class Scopes
{
    public const string ClaimType = "scope";

    public const string Read = "read";

    public const string AppsWrite = "apps:write";

    public const string ReleasesWrite = "releases:write";

    public static readonly string[] All = [Read, AppsWrite, ReleasesWrite];

    /// <summary>
    /// True for exactly the scopes that can be issued or required.
    /// </summary>
    public static bool IsValid(string scope)
        => Array.IndexOf(All, scope) >= 0;

    /// <summary>
    /// Drops unknown scopes and adds the implied read scope, in canonical order.
    /// </summary>
    public static string[] Expand(IEnumerable<string> scopes)
    {
        var set = new HashSet<string>(scopes.Where(IsValid), StringComparer.Ordinal);
        if (set.Count == 0)
        {
            return [];
        }

        if (set.Contains(AppsWrite) || set.Contains(ReleasesWrite))
        {
            set.Add(Read);
        }

        return [.. All.Where(set.Contains)];
    }
}
