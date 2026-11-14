using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FdroidMgr.Tests.Unit.Fixtures;

public static class TestDb
{
    public static FdroidDb Create()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FdroidDb>()
            .UseSqlite(conn)
            .Options;
        var db = new FdroidDb(options);
        db.Database.EnsureCreated();
        return db;
    }

    public static FdroidDb Reattach(FdroidDb db)
        => new(new DbContextOptionsBuilder<FdroidDb>()
            .UseSqlite(db.Database.GetDbConnection())
            .Options);

    public static async Task SeedRepoConfigAsync(FdroidDb db, string repoPath, string keyPath)
    {
        db.RepoConfig.RemoveRange(db.RepoConfig);
        db.RepoConfig.Add(new RepoConfig
        {
            Id = 1,
            RepoUrl = "http://localhost:5000/fdroid/repo",
            RepoName = "Test Repo",
            RepoDescription = "test",
            RepoPath = repoPath,
            KeyPath = keyPath,
        });
        await db.SaveChangesAsync();
    }

    public static async Task SeedKeyAsync(FdroidDb db, string packageName, string fingerprint, string? branch = null)
    {
        var key = new AppKey
        {
            PackageName = packageName,
            Alias = "release",
            Branch = branch,
            StorePath = "release.keystore",
            FingerprintSha256 = fingerprint,
            PasswordEncrypted = "iv:cipher",
        };
        db.Keys.Add(key);
        await db.SaveChangesAsync();
    }
}
