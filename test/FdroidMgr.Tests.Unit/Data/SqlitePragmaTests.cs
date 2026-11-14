using FdroidMgr.Api.Data;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Data;

public class SqlitePragmaTests : IDisposable
{
    private readonly string _dir;

    public SqlitePragmaTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"fdroidmgr-pragmas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task EnsureWalAsync_ConvertsRollbackDb_PersistentlyAndIdempotently()
    {
        var path = Path.Combine(_dir, "wal.db");
        await using var db = CreateFileDb(path);
        await db.Database.MigrateAsync();

        ForceRollbackJournal(path);
        (await db.EnsureWalAsync()).Should().BeTrue();

        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            (await cmd.ExecuteScalarAsync()).Should().Be("wal");
        }

        (await db.EnsureWalAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentRecordAsync_SameBucket_NoException_FinalCountIsTwo()
    {
        var path = Path.Combine(_dir, "stats.db");
        await using (var seed = CreateFileDb(path))
        {
            await seed.Database.MigrateAsync();
            await seed.EnsureWalAsync();
        }

        var svcA = Create(CreateFileDb(path));
        var svcB = Create(CreateFileDb(path));

        await Task.WhenAll(
            svcA.RecordAsync("com.example", 5),
            svcB.RecordAsync("com.example", 5));

        await using (var check = CreateFileDb(path))
        {
            var row = await check.DownloadCount.SingleAsync();
            row.Count.Should().Be(2);
        }
    }

    private static void ForceRollbackJournal(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE;";
        cmd.ExecuteScalar();
    }

    private static FdroidDb CreateFileDb(string path)
    {
        var options = new DbContextOptionsBuilder<FdroidDb>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new FdroidDb(options);
    }

    private static DownloadStatsService Create(FdroidDb db)
        => new(db, new FdroidRepoService(NullLogger<FdroidRepoService>.Instance, TestIo.Real, new RepoIndexSnapshot()),
            new DownloadStatsIndexCache(),
            NullLogger<DownloadStatsService>.Instance, TestIo.Real);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
