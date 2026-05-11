using Aircane.Domain.Common;

namespace Aircane.Domain.Entities;

/// <summary>
/// A reusable character sheet template saved from an imported character mapping.
/// Stores the field definitions (names, types, validation rules, display grouping,
/// and optional source coordinates) as a JSON blob.
/// </summary>
public class CharacterTemplate : EntityBase
{
    public string Name { get; set; }
    public string GameSystem { get; set; }
    public string Ruleset { get; set; }

    /// <summary>
    /// JSON array of <c>TemplateFieldDefinition</c> objects serialised from the application layer.
    /// </summary>
    public string FieldMappingsJson { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public CharacterTemplate(
        string name,
        string gameSystem,
        string ruleset,
        string fieldMappingsJson)
    {
        Name = name;
        GameSystem = gameSystem;
        Ruleset = ruleset;
        FieldMappingsJson = fieldMappingsJson;
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private CharacterTemplate() : base()
    {
        Name = string.Empty;
        GameSystem = string.Empty;
        Ruleset = string.Empty;
        FieldMappingsJson = "[]";
    }
}
