using System.Text;
using Aircane.Application.DTOs.Adventures;

namespace Aircane.Application.Adventures;

/// <summary>
/// Derives stable identifiers for adventure content that the generation pipeline does not number
/// itself. Scenes carry a <c>sceneId</c> from generation; NPCs, clues (handouts), secrets and
/// encounters do not, so solo execution addresses them by an ID derived from the stored content.
/// The derivation is deterministic, so the same adventure always yields the same IDs and runs
/// started before a restart keep resolving to the same content.
/// </summary>
public static class AdventureContentIds
{
    /// <summary>ID prefix for NPCs.</summary>
    public const string NpcPrefix = "npc-";

    /// <summary>ID prefix for clues (handouts the player can find).</summary>
    public const string CluePrefix = "clue-";

    /// <summary>ID prefix for secrets.</summary>
    public const string SecretPrefix = "secret-";

    /// <summary>ID prefix for encounters.</summary>
    public const string EncounterPrefix = "encounter-";

    /// <summary>
    /// Assigns an NPC ID to each NPC (<c>npc-{slug of name}</c>). NPCs sharing a name get a
    /// numeric suffix (<c>-2</c>, <c>-3</c>…) in list order so every ID is unique.
    /// </summary>
    public static IReadOnlyList<(string Id, GeneratedNpc Npc)> ForNpcs(IReadOnlyList<GeneratedNpc>? npcs)
    {
        var result = new List<(string, GeneratedNpc)>();
        if (npcs is null)
            return result;

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var npc in npcs)
        {
            var baseId = NpcPrefix + Slugify(npc.Name);
            seen.TryGetValue(baseId, out var count);
            seen[baseId] = ++count;
            result.Add((count == 1 ? baseId : $"{baseId}-{count}", npc));
        }

        return result;
    }

    /// <summary>Assigns <c>clue-N</c> IDs (1-based, list order) to the adventure's handouts.</summary>
    public static IReadOnlyList<(string Id, AdventureHandout Handout)> ForClues(AdventureClues? clues)
        => (clues?.Handouts ?? [])
            .Select((h, i) => ($"{CluePrefix}{i + 1}", h))
            .ToList();

    /// <summary>Assigns <c>secret-N</c> IDs (1-based, list order) to the adventure's secrets.</summary>
    public static IReadOnlyList<(string Id, AdventureSecret Secret)> ForSecrets(AdventureClues? clues)
        => (clues?.Secrets ?? [])
            .Select((s, i) => ($"{SecretPrefix}{i + 1}", s))
            .ToList();

    /// <summary>Assigns <c>encounter-N</c> IDs (1-based, list order) to the adventure's encounters.</summary>
    public static IReadOnlyList<(string Id, GeneratedEncounter Encounter)> ForEncounters(
        IReadOnlyList<GeneratedEncounter>? encounters)
        => (encounters ?? [])
            .Select((e, i) => ($"{EncounterPrefix}{i + 1}", e))
            .ToList();

    /// <summary>Lower-cases and hyphenates a name; falls back to <c>unnamed</c> when nothing is left.</summary>
    internal static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unnamed";

        var sb = new StringBuilder(value.Length);
        var lastWasHyphen = true; // suppresses a leading hyphen
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = sb.ToString().TrimEnd('-');
        return slug.Length == 0 ? "unnamed" : slug;
    }
}
