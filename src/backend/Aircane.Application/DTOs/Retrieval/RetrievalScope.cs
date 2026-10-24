namespace Aircane.Application.DTOs.Retrieval;

/// <summary>
/// The library scope rules retrieval searches for a campaign whose game system resolved to a
/// <c>GameSystemDefinition</c>. Produced by <c>IRetrievalScopeResolver</c>; when a campaign does
/// not resolve to a definition there is no scope and retrieval filters by the campaign's own strings.
/// </summary>
public sealed record RetrievalScope
{
    /// <summary>The resolved definition's id.</summary>
    public required Guid DefinitionId { get; init; }

    /// <summary>The resolved definition's identifier (e.g. "dnd-5e-2014").</summary>
    public required string DefinitionIdentifier { get; init; }

    /// <summary>The resolved definition's name.</summary>
    public required string DefinitionName { get; init; }

    /// <summary>
    /// Every spelling a document's game system may be filed under for this definition: its name,
    /// identifier and aliases. Compared case-insensitively.
    /// </summary>
    public required IReadOnlyList<string> GameSystems { get; init; }

    /// <summary>The definition's declared ruleset labels, as written (e.g. "2014").</summary>
    public required IReadOnlyList<string> DeclaredRulesets { get; init; }

    /// <summary>
    /// The normalized ruleset spellings a document may be filed under: the declared labels expanded
    /// through the known alias groups. Empty means no ruleset filter.
    /// </summary>
    public required IReadOnlyList<string> Rulesets { get; init; }

    /// <summary>
    /// The value shown on the "Ruleset:" line of the AI DM prompt: the first declared label, or the
    /// definition's name when it declares none.
    /// </summary>
    public string DisplayRuleset => DeclaredRulesets.Count > 0 ? DeclaredRulesets[0] : DefinitionName;
}
