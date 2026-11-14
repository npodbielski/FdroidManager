using System.Net.Http.Json;
using System.Text.Json;


namespace FdroidMgr.Tests.Component.Fixtures;

/// <summary>
/// Reader options matching the API's camelCase JSON, so tests can deserialize
/// responses into the same records the endpoints return.
/// </summary>
public static class TestJson
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static Task<T> ReadAsAsync<T>(this HttpContent content)
        => content.ReadFromJsonAsync<T>(Web)!;

    public static string Serialize(object value)
        => JsonSerializer.Serialize(value, Web);
}
