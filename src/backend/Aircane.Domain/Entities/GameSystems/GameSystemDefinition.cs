using Aircane.Domain.Common;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// The top-level container for a game system definition. Declaratively describes any TTRPG's
/// mechanics including dice conventions, resolution rules, character schema, conditions,
/// action economy, encounter budgeting, and AI guidance.
/// </summary>
public class GameSystemDefinition : EntityBase
{
    /// <summary>Unique slug identifier (e.g., "dnd-5e-2014", "shadowrun-6e").</summary>
    public string Identifier { get; set; }

    /// <summary>Display name of the game system.</summary>
    public string Name { get; set; }

    /// <summary>Semantic version of this definition (e.g., "1.0.0").</summary>
    public string Version { get; set; }

    /// <summary>Schema version this definition conforms to.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Publisher of the game system (e.g., "Wizards of the Coast").</summary>
    public string? Publisher { get; set; }

    /// <summary>Genre of the game system (e.g., "fantasy", "sci-fi", "horror").</summary>
    public string? Genre { get; set; }

    /// <summary>Description of the game system.</summary>
    public string? Description { get; set; }

    /// <summary>License type: "built-in", "user-created", or "community".</summary>
    public string License { get; set; }

    /// <summary>Tags for categorization and search (e.g., "d20", "fantasy", "levels").</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Dice conventions defining the system's primary and custom dice mechanics.</summary>
    public IReadOnlyList<DiceConvention> DiceConventions { get; set; } = [];

    /// <summary>Resolution rules defining how actions are resolved.</summary>
    public IReadOnlyList<ResolutionRule> ResolutionRules { get; set; } = [];

    /// <summary>Character schema defining the structure of character sheets.</summary>
    public CharacterSchema? CharacterSchema { get; set; }

    /// <summary>Condition set defining status effects and their mechanical impacts.</summary>
    public IReadOnlyList<ConditionDefinition> ConditionSet { get; set; } = [];

    /// <summary>Action economy definition for turn structure.</summary>
    public ActionEconomyDefinition? ActionEconomy { get; set; }

    /// <summary>Encounter budget formula for balancing encounters.</summary>
    public EncounterBudgetFormula? EncounterBudget { get; set; }

    /// <summary>AI guidance notes for the AI DM.</summary>
    public AiGuidance? AiGuidance { get; set; }

    /// <summary>Whether this definition is currently active and available for use.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Whether this is a built-in definition that cannot be deleted.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Full definition content stored as a JSON blob in the database.
    /// This is the serialized form of the entire definition including all sections.
    /// </summary>
    public string DefinitionJson { get; set; } = "{}";

    /// <summary>Last time this definition was updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public GameSystemDefinition(
        string identifier,
        string name,
        string version,
        int schemaVersion,
        string license,
        string? publisher = null,
        string? genre = null,
        string? description = null)
        : base()
    {
        Identifier = identifier;
        Name = name;
        Version = version;
        SchemaVersion = schemaVersion;
        License = license;
        Publisher = publisher;
        Genre = genre;
        Description = description;
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private GameSystemDefinition() : base()
    {
        Identifier = string.Empty;
        Name = string.Empty;
        Version = string.Empty;
        License = string.Empty;
    }
}
