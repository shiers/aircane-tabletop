using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Reads a free-text field the AI sometimes returns as a nested JSON object or array instead of a
/// string. Strings are used as-is; objects and arrays are kept as their compact JSON text so the
/// content is not lost and deserialization does not fail.
/// </summary>
public sealed class StringOrObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadText(ref reader);

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    internal static string? ReadText(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                using (var doc = JsonDocument.ParseValue(ref reader))
                    return doc.RootElement.GetRawText();
            default: // numbers / booleans
                using (var doc = JsonDocument.ParseValue(ref reader))
                    return doc.RootElement.GetRawText();
        }
    }
}

/// <summary>
/// Reads a list of strings where each item (or the whole value) may be a string or an object.
/// A lone string or object becomes a one-item list.
/// </summary>
public sealed class StringListOrObjectConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            return StringOrObjectConverter.ReadText(ref reader) is { } single ? [single] : [];

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (StringOrObjectConverter.ReadText(ref reader) is { } item)
                items.Add(item);
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
