using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FdroidMgr.Api.Data;

public class FdroidDb(DbContextOptions<FdroidDb> options) : DbContext(options)
{
    public DbSet<AppKey> Keys { get; set; } = null!;

    public DbSet<RepoConfig> RepoConfig { get; set; } = null!;

    public DbSet<ApiUser> ApiUsers { get; set; } = null!;

    public DbSet<ApiKey> ApiKeys { get; set; } = null!;

    public DbSet<FdroidClientKey> FdroidClientKey { get; set; } = null!;

    public DbSet<DownloadCount> DownloadCount { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppKey>(b =>
        {
            b.Property(x => x.PackageName).HasMaxLength(255);
            b.HasIndex(x => new { x.PackageName, x.Branch });
        });

        modelBuilder.Entity<ApiUser>(b =>
        {
            b.Property(x => x.Subject).HasMaxLength(512);
            b.HasIndex(x => x.Subject).IsUnique();
        });

        modelBuilder.Entity<ApiKey>(b =>
        {
            b.Property(x => x.KeyHash).HasMaxLength(64);
            b.Property(x => x.KeyPrefix).HasMaxLength(16);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Scopes).HasMaxLength(200);
            b.HasIndex(x => x.KeyHash).IsUnique();
            b.HasIndex(x => x.UserId);
            b.HasOne(x => x.User)
                .WithMany(u => u.ApiKeys)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FdroidClientKey>(b =>
        {
            b.Property(x => x.KeyHash).HasMaxLength(64);
            b.Property(x => x.KeyPrefix).HasMaxLength(16);
            b.HasIndex(x => x.KeyHash).IsUnique();
            b.HasOne(x => x.Creator)
                .WithOne()
                .HasForeignKey<FdroidClientKey>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadCount>(b =>
        {
            b.Property(x => x.DayUtc).HasMaxLength(10);
            b.Property(x => x.PackageName).HasMaxLength(255);
            b.HasIndex(x => new { x.DayUtc, x.PackageName, x.VersionCode })
                .IsUnique()
                .HasDatabaseName("IX_DownloadCount_DayUtc_PackageName_VersionCode");
        });
    }

    public async Task<bool> EnsureWalAsync(CancellationToken ct = default)
    {
        var conn = Database.GetDbConnection();
        var closeIt = conn.State != System.Data.ConnectionState.Open;
        if (closeIt)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using (var query = conn.CreateCommand())
            {
                query.CommandText = "PRAGMA journal_mode;";
                var mode = await query.ExecuteScalarAsync(ct);
                if (string.Equals(mode as string, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            await using var set = conn.CreateCommand();
            set.CommandText = "PRAGMA journal_mode=WAL;";
            await set.ExecuteNonQueryAsync(ct);
            return true;
        }
        finally
        {
            if (closeIt)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<bool> TableExistsAsync(string table, CancellationToken ct = default)
    {
        var conn = Database.GetDbConnection();
        var closeIt = conn.State != System.Data.ConnectionState.Open;
        if (closeIt)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $t";
            var p = cmd.CreateParameter();
            p.ParameterName = "$t";
            p.Value = table;
            cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is not null;
        }
        finally
        {
            if (closeIt)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct = default)
    {
        var conn = Database.GetDbConnection();
        var closeIt = conn.State != System.Data.ConnectionState.Open;
        if (closeIt)
        {
            await conn.OpenAsync(ct);
        }

        try
        {
            bool exists;
            await using (var query = conn.CreateCommand())
            {
                query.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using (var reader = await query.ExecuteReaderAsync(ct))
                {
                    exists = false;
                    while (await reader.ReadAsync(ct))
                    {
                        var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
            }

            if (!exists)
            {
                await using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
                await alter.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            if (closeIt)
            {
                await conn.CloseAsync();
            }
        }
    }

}
