using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Tests.Unit.Fixtures;

namespace FdroidMgr.Tests.Unit.Services;

public class ApiKeyServiceTests
{
    [Fact]
    public async Task ValidateAsync_FirstUse_StampsLastUsed()
    {
        await using var db = TestDb.Create();
        var svc = new ApiKeyService(db);
        var user = await svc.GetOrCreateUserAsync("subject-1", "alice");
        var (key, secret) = await svc.CreateAsync(user, "ci", ["read"], null);

        var principal = await svc.ValidateAsync(secret);

        principal.Should().NotBeNull();
        var stored = await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == key.Id);
        stored.LastUsedUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_WithinTheThreshold_LeavesLastUsedAlone()
    {
        await using var db = TestDb.Create();
        var (svc, key, secret) = await SeededAsync(db, ["read"]);
        var marker = DateTimeOffset.UtcNow.UtcDateTime - TimeSpan.FromSeconds(10);
        await StampAsync(db, key.Id, marker);

        await svc.ValidateAsync(secret);

        var stored = await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == key.Id);
        stored.LastUsedUtc.Should().BeCloseTo(marker, TimeSpan.FromSeconds(1),
            "a key seen a moment ago must not cause another UPDATE");
    }

    [Fact]
    public async Task ValidateAsync_AfterTheThreshold_StampsLastUsedAgain()
    {
        await using var db = TestDb.Create();
        var (svc, key, secret) = await SeededAsync(db, ["read"]);
        var marker = DateTimeOffset.UtcNow.UtcDateTime - ApiKeyService.LastUsedWriteThreshold - TimeSpan.FromMinutes(5);
        await StampAsync(db, key.Id, marker);

        await svc.ValidateAsync(secret);

        var stored = await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == key.Id);
        stored.LastUsedUtc.Should().BeAfter(marker);
    }

    [Fact]
    public async Task ValidateAsync_RevokedOrExpired_DoesNotStamp()
    {
        await using var db = TestDb.Create();
        var (svc, _, revokedSecret) = await SeededAsync(db, ["read"]);
        await db.ApiKeys.ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedUtc, DateTime.UtcNow));

        (await svc.ValidateAsync(revokedSecret)).Should().BeNull();

        var (_, expiredSecret) = await svc.CreateAsync(await svc.GetOrCreateUserAsync("subject-1", "alice"),
            "expired", ["read"], null);
        await db.ApiKeys.Where(k => k.Name == "expired")
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresUtc, DateTime.UtcNow.AddMinutes(-1)));

        (await svc.ValidateAsync(expiredSecret)).Should().BeNull();

        (await db.ApiKeys.AsNoTracking().Where(k => k.LastUsedUtc != null).AnyAsync())
            .Should().BeFalse("a key that does not authenticate is not a use");
    }

    [Fact]
    public async Task ValidateAsync_ClientKey_LastUsedIsThrottledToo()
    {
        await using var db = TestDb.Create();
        var svc = new ApiKeyService(db);
        var user = await svc.GetOrCreateUserAsync("subject-1", "alice");
        var secret = svc.GenerateSecret();
        var clientKey = new FdroidClientKey
        {
            UserId = user.Id,
            KeyHash = svc.Hash(secret),
            KeyPrefix = svc.Prefix(secret),
            SecretCipher = "iv:cipher",
        };
        db.FdroidClientKey.Add(clientKey);
        await db.SaveChangesAsync();

        await svc.ValidateAsync(secret);
        var first = (await db.FdroidClientKey.AsNoTracking().SingleAsync()).LastUsedUtc;
        first.Should().NotBeNull();

        var marker = DateTimeOffset.UtcNow.UtcDateTime - TimeSpan.FromSeconds(10);
        await db.FdroidClientKey.ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedUtc, marker));
        await svc.ValidateAsync(secret);

        var stored = await db.FdroidClientKey.AsNoTracking().SingleAsync();
        stored.LastUsedUtc.Should().BeCloseTo(marker, TimeSpan.FromSeconds(1));
    }

    private static async Task<(ApiKeyService Service, ApiKey Key, string Secret)> SeededAsync(FdroidDb db, string[] scopes)
    {
        var svc = new ApiKeyService(db);
        var user = await svc.GetOrCreateUserAsync("subject-1", "alice");
        var (key, secret) = await svc.CreateAsync(user, "ci", scopes, null);
        return (svc, key, secret);
    }

    private static Task StampAsync(FdroidDb db, int id, DateTime marker)
        => db.ApiKeys.Where(k => k.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedUtc, marker));
}
