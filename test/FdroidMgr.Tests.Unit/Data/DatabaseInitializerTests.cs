using FdroidMgr.Api.Data;
using FdroidMgr.Tests.Unit.Fixtures;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FdroidMgr.Tests.Unit.Data;

public class DatabaseInitializerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fdroidmgr-init-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Initialize_LegacyDatabase_IsBaselinedAndMigrated()
    {
        CreateLegacyDatabase();

        var db = Open();
        var init = new DatabaseInitializer();

        await init.InitializeAsync(db, (key, fallback) => key == "FDROID_REPO_PATH" ? "/srv/repo" : fallback, new FakeEnvironmentService());

        var columns = await ColumnNamesAsync(db, "RepoConfig");
        columns.Should().Contain(["RepoKeystorePassEnc", "RepoKeySha256", "BackupPath", "LastIndexBuiltUtc", "LastIndexError", "MasterKeySalt"]);
        (await ColumnNamesAsync(db, "Keys")).Should().Contain("PackageName");

        (await ScalarAsync(db, """
            SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030213141_InitialCreate';
            """)).Should().Be(1);

        var backfilled = await ScalarAsync(db, """
            SELECT "PackageName" FROM "Keys";
            """);
        backfilled.Should().Be("org.legacy.app");

        (await db.RepoConfig.AsNoTracking().SingleAsync()).RepoPath.Should().Be("/srv/repo");
    }

    [Fact]
    public async Task Initialize_RerunsAreIdempotent_AndSeedOnlyOnce()
    {
        CreateLegacyDatabase();
        var init = new DatabaseInitializer();

        var first = Open();
        await init.InitializeAsync(first, (_, fallback) => fallback, new FakeEnvironmentService());
        await init.InitializeAsync(first, (_, _) => "should-not-win", new FakeEnvironmentService());

        var reseeded = Open();
        (await reseeded.RepoConfig.CountAsync()).Should().Be(1);
        (await ScalarAsync(reseeded, """
            SELECT COUNT(*) FROM "Keys";
            """)).Should().Be(1);
    }

    [Fact]
    public async Task Initialize_EmptyDatabase_RunsMigrationsFromScratch()
    {
        var db = Open();

        await new DatabaseInitializer().InitializeAsync(db, (_, fallback) => fallback, new FakeEnvironmentService());

        (await db.RepoConfig.AsNoTracking().SingleAsync()).RepoPath.Should().Be(Path.Combine(Directory.GetCurrentDirectory(), "repo"));
        (await ScalarAsync(db, """
            SELECT COUNT(*) FROM "__EFMigrationsHistory";
            """)).Should().Be(2);
    }

    [Fact]
    public async Task PinPaths_RewritesStoredPathsOnlyWhenTheyDiffer()
    {
        var db = Open();
        var init = new DatabaseInitializer();
        await init.InitializeAsync(db, (_, fallback) => fallback, new FakeEnvironmentService());

        await init.PinPathsAsync(db, key => key == "FDROID_REPO_PATH" ? "/pinned/repo" : null);
        (await db.RepoConfig.AsNoTracking().SingleAsync()).RepoPath.Should().Be("/pinned/repo");

        await init.PinPathsAsync(db, key => key == "FDROID_KEY_PATH" ? "/pinned/keys" : "/pinned/repo");
        var cfg = await db.RepoConfig.AsNoTracking().SingleAsync();
        cfg.RepoPath.Should().Be("/pinned/repo");
        cfg.KeyPath.Should().Be("/pinned/keys");

        await init.PinPathsAsync(db, _ => null);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private static async Task<List<string>> ColumnNamesAsync(FdroidDb db, string table)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static async Task<object?> ScalarAsync(FdroidDb db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private FdroidDb Open()
    {
        var options = new DbContextOptionsBuilder<FdroidDb>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new FdroidDb(options);
    }

    private void CreateLegacyDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE "Apps" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "PackageName" TEXT NOT NULL);
            INSERT INTO "Apps" ("Id", "PackageName") VALUES (7, 'org.legacy.app');
            CREATE TABLE "Keys" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "AppId" INTEGER NULL,
                "Alias" TEXT NOT NULL,
                "StorePath" TEXT NOT NULL,
                "FingerprintSha256" TEXT NOT NULL,
                "PasswordEncrypted" TEXT NOT NULL,
                "CreatedUtc" TEXT NOT NULL
            );
            INSERT INTO "Keys" ("AppId", "Alias", "StorePath", "FingerprintSha256", "PasswordEncrypted", "CreatedUtc")
                VALUES (7, 'release', 'k.keystore', 'ab', 'legacy', '2026-01-01');
            CREATE TABLE "RepoConfig" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "RepoUrl" TEXT NOT NULL,
                "RepoName" TEXT NOT NULL,
                "RepoDescription" TEXT NOT NULL,
                "RepoPath" TEXT NOT NULL,
                "KeyPath" TEXT NOT NULL,
                "OAuthIssuer" TEXT NULL,
                "OAuthAudience" TEXT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
