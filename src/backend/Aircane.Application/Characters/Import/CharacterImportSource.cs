namespace Aircane.Application.Characters.Import;

/// <summary>
/// Identifies the external character-sheet format an import adapter recognises and maps.
/// </summary>
public enum CharacterImportSource
{
    /// <summary>The format could not be recognised.</summary>
    Unknown,

    /// <summary>D&amp;D Beyond character-service API JSON (saved as a file or fetched by URL).</summary>
    DndBeyondApi,

    /// <summary>D&amp;D Beyond companion / DDB-Importer export JSON.</summary>
    DndBeyondCompanion,

    /// <summary>Pathbuilder 2e export JSON.</summary>
    PathbuilderTwo,

    /// <summary>Foundry VTT dnd5e actor JSON.</summary>
    FoundryDnd5e,

    /// <summary>Foundry VTT pf2e actor JSON.</summary>
    FoundryPf2e,

    /// <summary>Roll20 character export JSON.</summary>
    Roll20,

    /// <summary>Generic / unknown VTT JSON handled by a best-effort fallback mapper.</summary>
    GenericVtt
}
