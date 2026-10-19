using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Reads an id field the AI sometimes returns as a number (<c>"sceneId": 3</c>) instead of the string
/// id it was given. Strings are used as-is, numbers become their literal text, and null stays null so
/// the stage can report the missing id by name. Mapping a numeric value to a real scene id happens
/// after parsing, where the earlier stage's ids are known.
/// </summary>
public sealed class IdStringConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadId(ref reader);

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    internal static string? ReadId(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => GetNumberText(ref reader),
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Expected an id as a string or number but found {reader.TokenType}."),
    };

    private static string GetNumberText(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.GetRawText();
    }
}

/// <summary>
/// Reads a list of ids where each item may be a string or a number. A lone id becomes a one-item
/// list and null becomes an empty list; null items are kept so the stage can report their position.
/// </summary>
public sealed class IdStringListConverter : JsonConverter<IReadOnlyList<string>>
{
    public override bool HandleNull => true;

    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            return [IdStringConverter.ReadId(ref reader)!];

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(IdStringConverter.ReadId(ref reader)!);
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
