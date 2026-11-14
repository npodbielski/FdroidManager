
namespace FdroidMgr.Api.Endpoints.Repo;

public static partial class RepoEndpoints
{
    public static void MapRepoApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/repo");
        MapGetRepoConfig(g);
        MapUpdateRepoConfig(g);
        MapRebuildIndex(g);
        MapInitStatus(g);
        MapInitRepo(g);
        MapRepoStatus(g);
        MapServeRepoFile(app);

    }

    private static string ContentType(string ext) => ext switch
    {
        ".apk" => "application/vnd.android.package-archive",
        ".xml" => "application/xml",
        ".json" => "application/json",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".jar" => "application/java-archive",
        ".css" => "text/css",
        ".html" => "text/html",
        _ => "application/octet-stream",
    };

    private static partial class Log
    {
        [LoggerMessage(EventId = 39, Level = LogLevel.Warning,
            Message = "could not read repo index to attribute APK download; serving anyway")]
        public static partial void DownloadAttributionFailed(ILogger logger, Exception ex);
    }
}
