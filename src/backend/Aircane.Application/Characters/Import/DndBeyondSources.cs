namespace Aircane.Application.Characters.Import;

/// <summary>
/// Well-known D&amp;D Beyond source-book ids that identify the 2024 ruleset. When a character's
/// classes cite any of these source ids, the import is treated as the 2024 ruleset; otherwise 2014.
/// </summary>
public static class DndBeyondSources
{
    /// <summary>
    /// Source ids that map to the 2024 ruleset (2024 Player's Handbook family). The list may be
    /// extended as additional 2024 publications are identified.
    /// </summary>
    public static readonly HashSet<int> Ruleset2024SourceIds = [672, 673, 674];
}
