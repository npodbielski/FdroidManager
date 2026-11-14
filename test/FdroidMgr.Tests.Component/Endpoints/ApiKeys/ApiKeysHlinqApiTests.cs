using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FdroidMgr.Tests.Component.Fixtures;

namespace FdroidMgr.Tests.Component.Endpoints.ApiKeys;

public class ApiKeysHlinqApiTests
{
    [Fact]
    public async Task BareRequest_ReturnsEveryKeyNewestFirst_AndNeverTheSecret()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");
            await CreateKey(client, "alpha", ["read"]);
            var secret = await CreateKey(client, "beta", ["read", "apps:write"]);

            var items = await Get(client, "/api/api-keys");
            Names(items).Should().Equal("beta", "alpha");

            items[0].GetProperty("keyPrefix").GetString().Should().Be(secret[..10]);
            items[0].ToString().Should().NotContain(secret[10..]);
            items[0].GetProperty("scopes").EnumerateArray().Select(s => s.GetString())
                .Should().Equal("read", "apps:write");
            items[0].TryGetProperty("keyHash", out _).Should().BeFalse();
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Where_Contains_FiltersServerSide()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");
            await CreateKey(client, "ci-pipeline", ["read"]);
            await CreateKey(client, "laptop", ["read"]);

            var items = await Get(client, "/api/api-keys?where[x.name.contains(pipeline)]");
            Names(items).Should().Equal("ci-pipeline");
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Where_Contains_IsCaseSensitive_OnSqlite()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");
            await CreateKey(client, "ci-pipeline", ["read"]);

            Names(await Get(client, "/api/api-keys?where[x.name.contains(PIPELINE)]")).Should().BeEmpty();
            Names(await Get(client, "/api/api-keys?where[x.name.contains(pipeline)]")).Should().Equal("ci-pipeline");
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Order_Take_Skip_PageTheResult()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");
            await CreateKey(client, "cc", ["read"]);
            await CreateKey(client, "aa", ["read"]);
            await CreateKey(client, "bb", ["read"]);

            Names(await Get(client, "/api/api-keys?orderBy[x.name].take[2]")).Should().Equal("aa", "bb");
            Names(await Get(client, "/api/api-keys?orderBy[x.name].skip[1].take[2]")).Should().Equal("bb", "cc");
            Names(await Get(client, "/api/api-keys?orderByDescending[x.name]")).Should().Equal("cc", "bb", "aa");
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task Count_ReturnsTheTotalNotThePage()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");
            await CreateKey(client, "aa", ["read"]);
            await CreateKey(client, "ab", ["read"]);
            await CreateKey(client, "zz", ["read"]);

            var total = await Get(client, "/api/api-keys?where[x.name.contains(a)].count[]");
            total.GetInt32().Should().Be(2);
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task TruncatedWhereAfterEntityAlias_IsBadRequest_NotAHang()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");

            string[] urls = ["/api/api-keys?where[x", "/api/api-keys?where[x."];
            foreach (var url in urls)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var bad = await client.GetAsync(url, cts.Token);
                bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }
        }
        finally { host.Dispose(); }
    }

    [Fact]
    public async Task UnknownField_IsRejectedAsBadRequest_Not500()
    {
        var host = NewHost();
        try
        {
            var client = host.Client("admin");
            await CreateKey(client, "aa", ["read"]);

            var bad = await client.GetAsync("/api/api-keys?where[");
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await bad.Content.ReadAsStringAsync();
            JsonDocument.Parse(body).RootElement.GetProperty("title")
                .GetString().Should().Contain("'where[' is invalid");
            JsonDocument.Parse(body).RootElement.GetProperty("status")
                .GetInt32().Should().Be(400);

            (await client.GetAsync("/api/api-keys?orderBy[x.keyHash]")).StatusCode
                .Should().Be(HttpStatusCode.BadRequest);
        }
        finally { host.Dispose(); }
    }

    private static StringContent Json(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static TestHost NewHost() => new(new TestHostOptions
    {
        DbName = "hlinq.db",
        DirPrefix = "fdroidmgr-hlinq",
    });

    private static async Task<string> CreateKey(HttpClient client, string name, string[] scopes)
    {
        var res = await client.PostAsync("/api/api-keys", Json(new { name, scopes }));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("key").GetString()!;
    }

    private static async Task<JsonElement> Get(HttpClient client, string url, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var res = await client.GetAsync(url);
        res.StatusCode.Should().Be(expect);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private static string[] Names(JsonElement items)
        => [.. items.EnumerateArray().Select(i => i.GetProperty("name").GetString()!)];
}
