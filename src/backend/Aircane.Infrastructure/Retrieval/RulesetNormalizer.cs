namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Normalizes free-text ruleset values so a filter still matches documents that were tagged with a
/// different spelling of the same ruleset ("2014", "5e 2014", "D&amp;D 5e 2014", ...).
/// </summary>
public static class RulesetNormalizer
{
    /// <summary>Groups of normalized spellings that all mean the same ruleset.</summary>
    private static readonly string[][] AliasGroups =
    [
        // D&D 5th Edition, 2014 rules
        ["2014", "5e 2014", "5e (2014)", "d&d 2014", "d&d 5e 2014", "dnd 2014", "dnd 5e 2014", "2014 rules"],
        // D&D 5th Edition, 2024 rules
        ["2024", "5e 2024", "5e (2024)", "5.5e", "one dnd", "one d&d", "onednd", "d&d 2024", "d&d 5e 2024",
            "dnd 2024", "dnd 5e 2024", "2024 rules"],
        // Pathfinder 2e Remaster
        ["remaster", "remastered", "pf2e remaster", "pathfinder remaster", "pathfinder 2e remaster", "2023 remaster"],
    ];

    /// <summary>Trims, lower-cases and collapses internal whitespace.</summary>
    public static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Returns every normalized spelling a document's ruleset may be stored under for the requested
    /// value: the value itself plus all members of its alias group, if it belongs to one.
    /// Compare against <c>lower(trim(Ruleset))</c>.
    /// </summary>
    public static IReadOnlyList<string> GetEquivalents(string ruleset)
    {
        var normalized = Normalize(ruleset);
        if (normalized.Length == 0)
            return [];

        var group = AliasGroups.FirstOrDefault(g => g.Contains(normalized));
        return group is null ? [normalized] : group;
    }
}
