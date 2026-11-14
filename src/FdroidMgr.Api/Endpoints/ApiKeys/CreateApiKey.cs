using FluentValidation;
using FdroidMgr.Api.Validation;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.ApiKeys;

public static class CreateApiKey
{
    public static void Map(IEndpointRouteBuilder g)
    {
        g.MapPost("", async (CreateApiKeyRequest req, IValidator<CreateApiKeyRequest> validator,
            IApiKeyService keys, IFdroidRepository db, HttpContext http,
            ICurrentUserService currentUser, CancellationToken ct) =>
        {
            if (validator.ValidateOrProblem(req) is { } problem)
            {
                return problem;
            }

            var user = await keys.GetOrCreateUserAsync(
                currentUser.GetSubject(http.User), currentUser.GetUsername(http.User), ct);
            var (key, plaintext) = await keys.CreateAsync(user, req.Name, req.Scopes ?? [], req.ExpiresDays, ct);

            var cfg = await db.FindRepoConfigAsync(ct);
            var repoUrl = cfg?.RepoUrl ?? "";
            var username = currentUser.GetUsername(http.User);
            var repoBasicAuthUrl = keys.BuildBasicAuthUrl(
                repoUrl, string.IsNullOrEmpty(username) ? "fdroid" : username, plaintext);

            return Results.Ok(new ApiKeyCreatedResponse(key.Id, plaintext, repoUrl, repoBasicAuthUrl));
        }).Produces<ApiKeyCreatedResponse>();
    }
}
