using System.Text.Json.Serialization;

namespace FdroidMgr.Api.Models;

/// <summary>
/// Body of a revealed or regenerated F-Droid client key; the plaintext is
/// returned in <c>key</c>. A plain reveal has no regeneration timestamp, so the
/// field is left out instead of being sent as null.
/// </summary>
public record ClientKeySecretResponse(
    string Key,
    string KeyPrefix,
    string RepoUrl,
    string RepoBasicAuthUrl,
    DateTime? LastRevealedUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTime? RegeneratedUtc = null);
