using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Data;

public class FdroidRepository(FdroidDb db) : IFdroidRepository
{
    public Task<RepoConfig> GetRepoConfigAsync(CancellationToken ct = default)
        => db.RepoConfig.AsNoTracking().FirstAsync(ct);

    public Task<RepoConfig?> FindRepoConfigAsync(CancellationToken ct = default)
        => db.RepoConfig.AsNoTracking().FirstOrDefaultAsync(ct);

    public Task<RepoConfig> TrackRepoConfigAsync(CancellationToken ct = default)
        => db.RepoConfig.FirstAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);

    public async Task<ApiUser?> FindUserBySubjectAsync(string subject, CancellationToken ct = default)
        => await db.ApiUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Subject == subject, ct);

    public async Task<List<ApiKey>> ListApiKeysAsync(int userId, CancellationToken ct = default)
        => await db.ApiKeys.AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.Id)
            .ToListAsync(ct);

    public async Task<List<AppKey>> ListKeysAsync(string packageName, CancellationToken ct = default)
        => await db.Keys.AsNoTracking()
            .Where(k => k.PackageName == packageName)
            .OrderBy(k => k.Id)
            .ToListAsync(ct);

    public async Task<List<string>> ListKeyFingerprintsAsync(string packageName, CancellationToken ct = default)
        => await db.Keys.AsNoTracking()
            .Where(k => k.PackageName == packageName)
            .Select(k => k.FingerprintSha256)
            .ToListAsync(ct);
}
