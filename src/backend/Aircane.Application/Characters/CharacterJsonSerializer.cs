using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aircane.Application.Characters;

/// <summary>
/// Helpers for serializing and deserializing canonical character JSON.
/// Uses a shared <see cref="JsonSerializerOptions"/> instance configured for
/// camelCase property names and enum string conversion.
/// </summary>
public static class CharacterJsonSerializer
{
    /// <summary>
    /// Shared options used for all character JSON operations.
    /// - camelCase property names for frontend compatibility.
    /// - Enums serialized as strings for readability.
    /// - Null values omitted to keep JSON compact.
    /// - Indented writing disabled for storage; use <see cref="IndentedOptions"/> for debugging.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    /// <summary>Options with indented output for debugging or display purposes.</summary>
    public static readonly JsonSerializerOptions IndentedOptions = new(Options)
    {
        WriteIndented = true
    };

    // ── CanonicalCharacter ────────────────────────────────────────────────────

    /// <summary>Serializes a <see cref="CanonicalCharacter"/> to a JSON string.</summary>
    public static string Serialize(CanonicalCharacter character)
        => JsonSerializer.Serialize(character, Options);

    /// <summary>
    /// Deserializes a JSON string to a <see cref="CanonicalCharacter"/>.
    /// Returns <c>null</c> if the input is null or whitespace.
    /// </summary>
    /// <exception cref="JsonException">Thrown when the JSON is malformed.</exception>
    public static CanonicalCharacter? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<CanonicalCharacter>(json, Options);
    }

    /// <summary>
    /// Deserializes a JSON string to a <see cref="CanonicalCharacter"/>.
    /// Returns a new default instance if the input is null, whitespace, or empty JSON object.
    /// </summary>
    public static CanonicalCharacter DeserializeOrDefault(string? json)
        => Deserialize(json) ?? new CanonicalCharacter();

    // ── CharacterCurrentState ─────────────────────────────────────────────────

    /// <summary>Serializes a <see cref="CharacterCurrentState"/> to a JSON string.</summary>
    public static string SerializeState(CharacterCurrentState state)
        => JsonSerializer.Serialize(state, Options);

    /// <summary>
    /// Deserializes a JSON string to a <see cref="CharacterCurrentState"/>.
    /// Returns <c>null</c> if the input is null or whitespace.
    /// </summary>
    public static CharacterCurrentState? DeserializeState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<CharacterCurrentState>(json, Options);
    }

    /// <summary>
    /// Deserializes a JSON string to a <see cref="CharacterCurrentState"/>.
    /// Returns a new default instance if the input is null or whitespace.
    /// </summary>
    public static CharacterCurrentState DeserializeStateOrDefault(string? json)
        => DeserializeState(json) ?? new CharacterCurrentState();

    // ── Round-trip helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Validates that a JSON string can be deserialized to a <see cref="CanonicalCharacter"/>
    /// without throwing. Returns false if the JSON is malformed.
    /// </summary>
    public static bool TryDeserialize(string? json, out CanonicalCharacter? character)
    {
        character = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            character = JsonSerializer.Deserialize<CanonicalCharacter>(json, Options);
            return character is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
