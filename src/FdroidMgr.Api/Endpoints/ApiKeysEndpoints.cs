using FdroidMgr.Api.Endpoints.ApiKeys;

namespace FdroidMgr.Api.Endpoints;

public static class ApiKeysEndpoints
{
    public static void MapApiKeysApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/api-keys").RequireAuthorization("InteractiveOnly");

        GetApiKeys.Map(g);
        CreateApiKey.Map(g);
        RevokeApiKey.Map(g);

    }
}
