using FdroidMgr.Api.Services;
using HamsterWheel.HLinq.Request;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Endpoints.Apps;

public static partial class AppsEndpoints
{
    private static void MapListApps(IEndpointRouteBuilder g)
    {
        g.MapGet("", async (HLinqQuery<AppListItem> query, IFdroidRepository db, IFdroidRepoService repo, IOService io, IFdroidMetadataService metadata, CancellationToken ct) =>
        {
            var cfg = await Cfg(db, ct);
            var index = await repo.ReadIndexAsync(cfg, ct);
            var v2Icons = await repo.ReadV2IconsAsync(cfg, ct);

            var items = new List<AppListItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in index?.Apps ?? new List<FdroidApp>())
            {
                seen.Add(a.PackageName);
                var v2 = v2Icons.TryGetValue(a.PackageName, out var v) ? v : null;
                items.Add(ToItem(a, ResolveIcon(a.Icon, v2)));
            }

            var repoRoot = metadata.ResolveRepoRoot(cfg);
            var metadataDir = Path.Combine(repoRoot, "metadata");
            if (!io.DirectoryExists(metadataDir))
            {
                return Results.Ok(query.ApplyTo(items.AsQueryable(), ct));
            }

            foreach (var file in io.EnumerateFiles(metadataDir, "*.yml"))
            {
                var pkg = Path.GetFileNameWithoutExtension(file);
                if (!seen.Add(pkg))
                {
                    continue;
                }

                var fields = FdroidMetadataService.ParseAppMetadata(await io.ReadAllTextAsync(file, ct));
                items.Add(new AppListItem(pkg, fields.Name, fields.Summary, fields.License, null, 0, null, null));
            }

            return Results.Ok(query.ApplyTo(items.AsQueryable(), ct));
        }).Produces<IEnumerable<AppListItem>>();
    }
}
