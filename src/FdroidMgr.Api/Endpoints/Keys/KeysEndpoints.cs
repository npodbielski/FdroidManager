using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Endpoints.Keys;

public static partial class KeysEndpoints
{
    public static void MapKeysApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/apps/{packageName}/keys").RequireAuthorization(Scopes.Read);
        MapGenerateKey(g);
        MapImportKey(g);
        MapListAppKeys(g);

    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 37, Level = LogLevel.Warning,
            Message = "package {PackageName} has releases but no recorded signer in the index; accepting import without a signer comparison")]
        public static partial void ImportWithoutSignerComparison(ILogger logger, string packageName);
    }
}
