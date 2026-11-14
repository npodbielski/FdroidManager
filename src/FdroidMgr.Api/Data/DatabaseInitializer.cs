using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Data;

public class DatabaseInitializer : IDatabaseInitializer
{
    private const string InitialMigrationId = "20261030213141_InitialCreate";

    private const string InitialProductVersion = "10.0.12";

    public async Task InitializeAsync(FdroidDb db, Func<string, string, string> resolve, IEnvironmentService env, CancellationToken ct = default)
    {
        await MigrateOrBaselineAsync(db, ct);
        await db.EnsureWalAsync(ct);

        if (await db.TableExistsAsync("Apps", ct))
        {
            await BackfillPackageNamesAsync(db, ct);
        }

        await SeedRepoConfigAsync(db, resolve, env, ct);
    }

    private async Task MigrateOrBaselineAsync(FdroidDb db, CancellationToken ct)
    {
        if (!await db.TableExistsAsync("__EFMigrationsHistory", ct) && await db.TableExistsAsync("RepoConfig", ct))
        {
            await db.EnsureColumnAsync("RepoConfig", "RepoKeystorePassEnc", "TEXT NULL", ct);
            await db.EnsureColumnAsync("RepoConfig", "RepoKeySha256", "TEXT NULL", ct);
            await db.EnsureColumnAsync("RepoConfig", "BackupPath", "TEXT NULL", ct);
            await db.EnsureColumnAsync("RepoConfig", "LastIndexBuiltUtc", "TEXT NULL", ct);
            await db.EnsureColumnAsync("RepoConfig", "LastIndexError", "TEXT NULL", ct);
            await db.EnsureColumnAsync("Keys", "PackageName", "TEXT NULL", ct);
            await StampBaselineAsync(db, ct);
        }

        await db.Database.MigrateAsync(ct);
    }

    private async Task StampBaselineAsync(FdroidDb db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var closeIt = conn.State != System.Data.ConnectionState.Open;
        if (closeIt)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """;
            await create.ExecuteNonQueryAsync(ct);

            await using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ($id, $pv);
                """;
            var id = insert.CreateParameter();
            id.ParameterName = "$id";
            id.Value = InitialMigrationId;
            var pv = insert.CreateParameter();
            pv.ParameterName = "$pv";
            pv.Value = InitialProductVersion;
            insert.Parameters.Add(id);
            insert.Parameters.Add(pv);
            await insert.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (closeIt)
            {
                await conn.CloseAsync();
            }
        }
    }

    private async Task BackfillPackageNamesAsync(FdroidDb db, CancellationToken ct)
        => await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Keys" SET "PackageName" = (SELECT "PackageName" FROM "Apps" WHERE "Apps"."Id" = "Keys"."AppId")
              WHERE ("PackageName" IS NULL OR "PackageName" = '') AND "AppId" IS NOT NULL;
            """, ct);

    private async Task SeedRepoConfigAsync(FdroidDb db, Func<string, string, string> resolve, IEnvironmentService env, CancellationToken ct)
    {
        if (await db.RepoConfig.AnyAsync(ct))
        {
            return;
        }

        db.RepoConfig.Add(new RepoConfig
        {
            RepoPath = resolve("FDROID_REPO_PATH", Path.Combine(env.GetCurrentDirectory(), "repo")),
            KeyPath = resolve("FDROID_KEY_PATH", Path.Combine(env.GetCurrentDirectory(), "keys")),
            RepoUrl = resolve("FDROID_REPO_URL", "http://localhost:5000/fdroid/repo"),
            RepoName = resolve("FDROID_REPO_NAME", "My F-Droid Repository"),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task PinPathsAsync(FdroidDb db, Func<string, string?> envOrConfig, CancellationToken ct = default)
    {
        var repoCfg = await db.RepoConfig.FirstOrDefaultAsync(ct);
        if (repoCfg is null)
        {
            return;
        }

        var pinnedRepoPath = envOrConfig("FDROID_REPO_PATH");
        var pinnedKeyPath = envOrConfig("FDROID_KEY_PATH");
        var changed = false;
        if (!string.IsNullOrWhiteSpace(pinnedRepoPath) && repoCfg.RepoPath != pinnedRepoPath)
        {
            repoCfg.RepoPath = pinnedRepoPath;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(pinnedKeyPath) && repoCfg.KeyPath != pinnedKeyPath)
        {
            repoCfg.KeyPath = pinnedKeyPath;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
