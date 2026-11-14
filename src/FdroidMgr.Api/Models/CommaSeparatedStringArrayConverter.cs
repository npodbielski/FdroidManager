using System.Text.Json;
using System.Text.Json.Serialization;

namespace FdroidMgr.Api.Models;

public sealed class CommaSeparatedStringArrayConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var scope in value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            writer.WriteStringValue(scope);
        }

        writer.WriteEndArray();
    }
}
