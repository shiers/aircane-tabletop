using System.Text.Json;
using System.Text.Json.Serialization;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Custom JSON converter that maps the Game System Definition JSON format
/// (with nested metadata, diceConventions object, resolutionRules object, etc.)
/// to the flat GameSystemDefinition domain model.
/// </summary>
internal class GameSystemDefinitionJsonConverter : JsonConverter<GameSystemDefinition>
{
    private static readonly JsonSerializerOptions InnerOptions = CreateInnerOptions();

    private static JsonSerializerOptions CreateInnerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    public override GameSystemDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Extract metadata
        var metadata = root.TryGetProperty("metadata", out var metadataElement)
            ? metadataElement
            : (JsonElement?)null;

        var identifier = metadata?.TryGetStringProperty("id") ?? string.Empty;
        var name = metadata?.TryGetStringProperty("name") ?? string.Empty;
        var version = metadata?.TryGetStringProperty("version") ?? string.Empty;
        var publisher = metadata?.TryGetStringProperty("publisher");
        var genre = metadata?.TryGetStringProperty("genre");
        var description = metadata?.TryGetStringProperty("description");
        var license = metadata?.TryGetStringProperty("license") ?? "user-created";
        var tags = metadata.HasValue && metadata.Value.TryGetProperty("tags", out var tagsElement)
            ? DeserializeList<string>(tagsElement)
            : new List<string>();

        // Extract schema version
        var schemaVersion = 1;
        if (root.TryGetProperty("schema_version", out var svElement) ||
            root.TryGetProperty("schemaVersion", out svElement))
        {
            if (svElement.ValueKind == JsonValueKind.Number)
                schemaVersion = svElement.GetInt32();
        }

        var definition = new GameSystemDefinition(
            identifier: identifier,
            name: name,
            version: version,
            schemaVersion: schemaVersion,
            license: license,
            publisher: publisher,
            genre: genre,
            description: description)
        {
            Tags = tags
        };

        // Parse dice conventions
        if (root.TryGetProperty("dice_conventions", out var diceElement) ||
            root.TryGetProperty("diceConventions", out diceElement))
        {
            definition.DiceConventions = ParseDiceConventions(diceElement);
        }

        // Parse resolution rules
        if (root.TryGetProperty("resolution_rules", out var rulesElement) ||
            root.TryGetProperty("resolutionRules", out rulesElement))
        {
            definition.ResolutionRules = ParseResolutionRules(rulesElement);
        }

        // Parse character schema
        if (root.TryGetProperty("character_schema", out var schemaElement) ||
            root.TryGetProperty("characterSchema", out schemaElement))
        {
            definition.CharacterSchema = DeserializeElement<CharacterSchema>(schemaElement);
        }

        // Parse condition set
        if (root.TryGetProperty("condition_set", out var conditionSetElement) ||
            root.TryGetProperty("conditionSet", out conditionSetElement))
        {
            if (conditionSetElement.TryGetProperty("conditions", out var conditionsArray))
            {
                definition.ConditionSet = DeserializeList<ConditionDefinition>(conditionsArray);
            }
        }

        // Parse action economy
        if (root.TryGetProperty("action_economy", out var actionElement) ||
            root.TryGetProperty("actionEconomy", out actionElement))
        {
            definition.ActionEconomy = DeserializeElement<ActionEconomyDefinition>(actionElement);
        }

        // Parse encounter budget
        if (root.TryGetProperty("encounter_budget", out var budgetElement) ||
            root.TryGetProperty("encounterBudget", out budgetElement))
        {
            definition.EncounterBudget = DeserializeElement<EncounterBudgetFormula>(budgetElement);
        }

        // Parse AI guidance
        if (root.TryGetProperty("ai_guidance", out var aiElement) ||
            root.TryGetProperty("aiGuidance", out aiElement))
        {
            definition.AiGuidance = DeserializeElement<AiGuidance>(aiElement);
        }

        return definition;
    }

    public override void Write(Utf8JsonWriter writer, GameSystemDefinition value, JsonSerializerOptions options)
    {
        // Serialization is handled by the printer (task 3.2), not this converter
        throw new NotSupportedException("Use the GameSystemDefinitionPrinter for serialization.");
    }

    private static IReadOnlyList<DiceConvention> ParseDiceConventions(JsonElement element)
    {
        var conventions = new List<DiceConvention>();

        // Parse "primary" convention
        if (element.TryGetProperty("primary", out var primaryElement))
        {
            var primary = DeserializeElement<DiceConvention>(primaryElement);
            if (primary is not null)
            {
                conventions.Add(primary with { Name = string.IsNullOrEmpty(primary.Name) ? "primary" : primary.Name });
            }
        }

        // Parse named conventions at the top level (e.g., "damage")
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name == "primary" || prop.Name == "custom")
                continue;

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                var conv = DeserializeElement<DiceConvention>(prop.Value);
                if (conv is not null)
                {
                    conventions.Add(conv with { Name = string.IsNullOrEmpty(conv.Name) ? prop.Name : conv.Name });
                }
            }
        }

        // Parse "custom" array
        if (element.TryGetProperty("custom", out var customElement) &&
            customElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in customElement.EnumerateArray())
            {
                var conv = DeserializeElement<DiceConvention>(item);
                if (conv is not null)
                    conventions.Add(conv);
            }
        }

        return conventions;
    }

    private static IReadOnlyList<ResolutionRule> ParseResolutionRules(JsonElement element)
    {
        var rules = new List<ResolutionRule>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var rule = DeserializeElement<ResolutionRule>(prop.Value);
                if (rule is not null)
                {
                    // The property key becomes the rule's Name
                    rules.Add(rule with { Name = string.IsNullOrEmpty(rule.Name) ? prop.Name : rule.Name });
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            rules = DeserializeList<ResolutionRule>(element);
        }

        return rules;
    }

    private static T? DeserializeElement<T>(JsonElement element)
    {
        var json = element.GetRawText();
        return JsonSerializer.Deserialize<T>(json, InnerOptions);
    }

    private static List<T> DeserializeList<T>(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return new List<T>();

        var json = element.GetRawText();
        return JsonSerializer.Deserialize<List<T>>(json, InnerOptions) ?? new List<T>();
    }
}

/// <summary>
/// Extension methods for JsonElement to simplify property access.
/// </summary>
internal static class JsonElementExtensions
{
    public static string? TryGetStringProperty(this JsonElement? element, string propertyName)
    {
        if (!element.HasValue)
            return null;

        return element.Value.TryGetStringProperty(propertyName);
    }

    public static string? TryGetStringProperty(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();

        return null;
    }
}
