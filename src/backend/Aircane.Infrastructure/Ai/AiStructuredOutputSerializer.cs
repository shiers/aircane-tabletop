using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Deserializes the model's structured JSON reply into <see cref="AiStructuredOutput"/>.
/// <para>
/// The wire contract of the chat providers is snake_case (<c>proposed_actions</c>), but the AI DM
/// system prompt documents camelCase (<c>proposedActions</c>) and action types by name
/// (<c>"type": "advanceScene"</c>). Models follow the prompt, so a strict snake_case/numeric-enum
/// read silently dropped every proposed action. This reader accepts either key style at any depth
/// and either string or numeric enum values, so both the documented and the legacy shape work.
/// </para>
/// </summary>
internal static class AiStructuredOutputSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Reading is case-insensitive, so "advanceScene" and "AdvanceScene" both bind; numbers still bind too.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Deserializes <paramref name="json"/>; returns null when it is the JSON literal <c>null</c>.</summary>
    /// <exception cref="JsonException">The text is not valid JSON or does not match the shape.</exception>
    public static AiStructuredOutput? Deserialize(string json)
    {
        var node = JsonNode.Parse(json);
        return Normalize(node)?.Deserialize<AiStructuredOutput>(Options);
    }

    /// <summary>Rewrites every object key to snake_case (a no-op for keys that already are).</summary>
    private static JsonNode? Normalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                    result[JsonNamingPolicy.SnakeCaseLower.ConvertName(key)] = Normalize(value);
                return result;
            }
            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array)
                    result.Add(Normalize(item));
                return result;
            }
            default:
                return node?.DeepClone();
        }
    }
}
