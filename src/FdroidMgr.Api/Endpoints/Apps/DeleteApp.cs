using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapDeleteApp(IEndpointRouteBuilder g)
    {
        g.MapDelete("/{packageName}", async (string packageName, IFdroidRepository db, IFdroidRepoService repo,
            IFdroidMetadataService metadata, IRepoIndexService index, IOService io, CancellationToken ct) =>
        {
            var cfg = await Cfg(db, ct);
            var index0 = await repo.ReadIndexAsync(cfg, ct);
            var current = await ResolveAppFieldsAsync(packageName, index0, metadata, ct);
            if (current is null)
            {
                return Results.NotFound(new ErrorResponse("app not found"));
            }

            var repoRoot = metadata.ResolveRepoRoot(cfg);
            var yml = metadata.GetPath(packageName, repoRoot);
            if (yml is not null && io.FileExists(yml))
            {
                io.DeleteFile(yml);
            }

            foreach (var apk in io.EnumerateFiles(cfg.RepoPath, packageName + "_*.apk"))
            {
                io.DeleteFile(apk);
            }

            var changelogDir = Path.Combine(repoRoot, "metadata", packageName);
            if (io.DirectoryExists(changelogDir))
            {
                try
                {
                    io.DeleteDirectory(changelogDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            await index.GenerateStrictAsync(cfg, ct: ct);

            return Results.NoContent();
        }).RequireAuthorization(Scopes.AppsWrite)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }
}
