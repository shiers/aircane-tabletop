using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Pathfinder 2e (Remaster) encounter difficulty validator. Unlike D&amp;D 5e (which uses a
/// fixed CR-to-XP table and encounter multipliers), PF2e assigns each creature an XP value by
/// its <em>level relative to the party level</em>, sums those, and compares the total against
/// per-encounter budget thresholds scaled by party size.
/// </summary>
/// <remarks>
/// Mirrors the interface of <see cref="Dnd5eEncounterValidator"/> so the two are interchangeable
/// behind the <see cref="IEncounterValidator"/> abstraction. Numbers follow the PF2e GM Core
/// building-encounters table (party of four baseline, adjusted per extra/missing character).
/// </remarks>
public sealed class Pf2eEncounterValidator : IEncounterValidator
{
    private readonly ILogger<Pf2eEncounterValidator> _logger;

    /// <summary>
    /// Creature XP by level relative to the party level (PF2e GM Core). Level differences below
    /// −4 are worth 0 (trivial threat); above +4 are capped at the +4 value.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, int> XpByLevelDifference = new Dictionary<int, int>
    {
        [-4] = 10,
        [-3] = 15,
        [-2] = 20,
        [-1] = 30,
        [0] = 40,
        [1] = 60,
        [2] = 80,
        [3] = 120,
        [4] = 160,
    };

    /// <summary>
    /// Per-encounter XP budget thresholds for a party of four (PF2e GM Core), and the per-extra
    /// (or per-missing) character adjustment applied to scale to other party sizes.
    /// </summary>
    internal static readonly (string Name, int BudgetForPartyOfFour, int PerCharacterAdjustment)[] Tiers =
    [
        ("Trivial", 40, 10),
        ("Low", 60, 15),
        ("Moderate", 80, 20),
        ("Severe", 120, 30),
        ("Extreme", 160, 40),
    ];

    private const int BaselinePartySize = 4;

    public Pf2eEncounterValidator(ILogger<Pf2eEncounterValidator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public EncounterValidationResult ValidateEncounter(
        GeneratedEncounter encounter,
        int partySize,
        int averageLevel)
    {
        var warnings = new List<string>();

        partySize = Math.Max(1, partySize);
        averageLevel = Math.Clamp(averageLevel, 1, 25);

        var totalXp = 0;
        var totalCreatureCount = 0;
        var hasUnparsableLevel = false;

        foreach (var creature in encounter.Enemies)
        {
            var count = Math.Max(1, creature.Count);
            totalCreatureCount += count;

            if (TryParseCreatureLevel(creature.ChallengeRating, out var creatureLevel))
            {
                var xpPer = XpForCreatureLevel(creatureLevel, averageLevel);
                totalXp += xpPer * count;
            }
            else
            {
                hasUnparsableLevel = true;
                warnings.Add(
                    $"Could not parse creature level '{creature.ChallengeRating}' for '{creature.Name}'. XP estimate may be inaccurate.");
            }
        }

        if (hasUnparsableLevel && totalXp == 0)
        {
            return new EncounterValidationResult
            {
                IsValid = true,
                EstimatedDifficulty = "unknown",
                Warnings = ["Unable to estimate difficulty: no creature levels could be parsed."],
                TotalXP = 0,
                AdjustedXP = 0,
            };
        }

        var thresholds = GetPartyThresholds(partySize);
        var estimatedDifficulty = DetermineTier(totalXp, thresholds);

        var isValid = true;

        var requestedDifficulty = NormalizeDifficulty(encounter.Difficulty);
        if (requestedDifficulty != "unknown")
        {
            var difference = TierRank(estimatedDifficulty) - TierRank(requestedDifficulty);
            if (difference >= 2)
            {
                isValid = false;
                warnings.Add(
                    $"Encounter is significantly harder than requested. Requested '{requestedDifficulty}' but estimated '{estimatedDifficulty}' ({totalXp} XP).");
            }
            else if (difference <= -2)
            {
                isValid = false;
                warnings.Add(
                    $"Encounter is significantly easier than requested. Requested '{requestedDifficulty}' but estimated '{estimatedDifficulty}' ({totalXp} XP).");
            }
            else if (difference == 1)
            {
                warnings.Add($"Encounter is slightly harder than requested '{requestedDifficulty}'. Estimated '{estimatedDifficulty}'.");
            }
            else if (difference == -1)
            {
                warnings.Add($"Encounter is slightly easier than requested '{requestedDifficulty}'. Estimated '{estimatedDifficulty}'.");
            }
        }

        if (estimatedDifficulty == "extreme" && partySize <= 2)
        {
            warnings.Add("Extreme-threat encounter for a small party (2 or fewer). Consider reducing the threat.");
        }

        if (totalCreatureCount == 0)
        {
            isValid = false;
            warnings.Add("Encounter has no enemies.");
        }

        _logger.LogDebug(
            "PF2e encounter '{Title}': {CreatureCount} creatures, {TotalXP} XP vs party of {PartySize} " +
            "(Trivial/Low/Moderate/Severe/Extreme = {T}/{L}/{M}/{S}/{E}). Estimated: {Difficulty}",
            encounter.Title, totalCreatureCount, totalXp, partySize,
            thresholds.Trivial, thresholds.Low, thresholds.Moderate, thresholds.Severe, thresholds.Extreme,
            estimatedDifficulty);

        return new EncounterValidationResult
        {
            IsValid = isValid,
            EstimatedDifficulty = estimatedDifficulty,
            Warnings = warnings,
            TotalXP = totalXp,
            // PF2e has no monster-count multiplier; adjusted XP mirrors the summed budget XP.
            AdjustedXP = totalXp,
        };
    }

    /// <summary>
    /// Returns the XP a single creature contributes given its level and the party's average
    /// level, following the PF2e level-difference table (clamped to [−4, +4]).
    /// </summary>
    public static int XpForCreatureLevel(int creatureLevel, int partyLevel)
    {
        var diff = creatureLevel - partyLevel;
        if (diff < -4)
            return 0;
        if (diff > 4)
            diff = 4;
        return XpByLevelDifference[diff];
    }

    /// <summary>
    /// Parses a PF2e creature level from a free-text field (e.g. "3", "Level 3", "Lvl -1", "CL 5").
    /// </summary>
    public static bool TryParseCreatureLevel(string? value, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        foreach (var prefix in new[] { "level", "lvl", "cl", "cr" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..].Trim();
                break;
            }
        }

        return int.TryParse(normalized, out level);
    }

    /// <summary>
    /// Returns the per-encounter XP thresholds for each PF2e difficulty tier, scaled from the
    /// party-of-four baseline by the party's actual size.
    /// </summary>
    public static (int Trivial, int Low, int Moderate, int Severe, int Extreme) GetPartyThresholds(int partySize)
    {
        var delta = partySize - BaselinePartySize;
        int Scaled(int index) =>
            Math.Max(0, Tiers[index].BudgetForPartyOfFour + Tiers[index].PerCharacterAdjustment * delta);

        return (Scaled(0), Scaled(1), Scaled(2), Scaled(3), Scaled(4));
    }

    private static string DetermineTier(
        int totalXp,
        (int Trivial, int Low, int Moderate, int Severe, int Extreme) t)
    {
        if (totalXp >= t.Extreme)
            return "extreme";
        if (totalXp >= t.Severe)
            return "severe";
        if (totalXp >= t.Moderate)
            return "moderate";
        if (totalXp >= t.Low)
            return "low";
        return "trivial";
    }

    private static string NormalizeDifficulty(string? difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
            return "unknown";

        return difficulty.Trim().ToLowerInvariant() switch
        {
            "trivial" => "trivial",
            "low" => "low",
            "moderate" => "moderate",
            "severe" => "severe",
            "extreme" => "extreme",
            // Tolerate D&D-style requests by mapping to the nearest PF2e tier.
            "easy" => "low",
            "medium" => "moderate",
            "hard" => "severe",
            "deadly" => "extreme",
            _ => "unknown",
        };
    }

    private static int TierRank(string tier) => tier switch
    {
        "trivial" => 0,
        "low" => 1,
        "moderate" => 2,
        "severe" => 3,
        "extreme" => 4,
        _ => 2,
    };
}
