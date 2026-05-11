using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Placeholder D&amp;D 5e 2014 encounter difficulty validator.
/// Uses the standard CR-to-XP table, encounter multipliers, and party XP thresholds
/// to estimate encounter difficulty and flag obviously unsuitable encounters.
/// </summary>
public sealed class Dnd5eEncounterValidator : IEncounterValidator
{
    private readonly ILogger<Dnd5eEncounterValidator> _logger;

    /// <summary>
    /// Standard D&amp;D 5e CR-to-XP mapping.
    /// Keys are CR strings (e.g., "0", "1/8", "1/4", "1/2", "1", "2", ..., "30").
    /// </summary>
    internal static readonly Dictionary<string, int> CrToXpTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0"] = 10,
        ["1/8"] = 25,
        ["1/4"] = 50,
        ["1/2"] = 100,
        ["1"] = 200,
        ["2"] = 450,
        ["3"] = 700,
        ["4"] = 1100,
        ["5"] = 1800,
        ["6"] = 2300,
        ["7"] = 2900,
        ["8"] = 3900,
        ["9"] = 5000,
        ["10"] = 5900,
        ["11"] = 7200,
        ["12"] = 8400,
        ["13"] = 10000,
        ["14"] = 11500,
        ["15"] = 13000,
        ["16"] = 15000,
        ["17"] = 18000,
        ["18"] = 20000,
        ["19"] = 22000,
        ["20"] = 25000,
        ["21"] = 33000,
        ["22"] = 41000,
        ["23"] = 50000,
        ["24"] = 62000,
        ["25"] = 75000,
        ["26"] = 90000,
        ["27"] = 105000,
        ["28"] = 120000,
        ["29"] = 135000,
        ["30"] = 155000,
    };

    /// <summary>
    /// XP thresholds per character level for each difficulty category.
    /// Index 0 = level 1, index 19 = level 20.
    /// Each entry is (Easy, Medium, Hard, Deadly).
    /// </summary>
    internal static readonly (int Easy, int Medium, int Hard, int Deadly)[] XpThresholdsByLevel =
    [
        (25, 50, 75, 100),       // Level 1
        (50, 100, 150, 200),     // Level 2
        (75, 150, 225, 400),     // Level 3
        (125, 250, 375, 500),    // Level 4
        (250, 500, 750, 1100),   // Level 5
        (300, 600, 900, 1400),   // Level 6
        (350, 750, 1100, 1700),  // Level 7
        (450, 900, 1400, 2100),  // Level 8
        (550, 1100, 1600, 2400), // Level 9
        (600, 1200, 1900, 2800), // Level 10
        (800, 1600, 2400, 3600), // Level 11
        (1000, 2000, 3000, 4500), // Level 12
        (1100, 2200, 3400, 5100), // Level 13
        (1250, 2500, 3800, 5700), // Level 14
        (1400, 2800, 4300, 6400), // Level 15
        (1600, 3200, 4800, 7200), // Level 16
        (2000, 3900, 5900, 8800), // Level 17
        (2100, 4200, 6300, 9500), // Level 18
        (2400, 4900, 7300, 10900), // Level 19
        (2800, 5700, 8500, 12700), // Level 20
    ];

    /// <summary>
    /// Encounter multipliers based on number of monsters.
    /// </summary>
    internal static readonly (int MinMonsters, int MaxMonsters, double Multiplier)[] EncounterMultipliers =
    [
        (1, 1, 1.0),
        (2, 2, 1.5),
        (3, 6, 2.0),
        (7, 10, 2.5),
        (11, 14, 3.0),
        (15, int.MaxValue, 4.0),
    ];

    public Dnd5eEncounterValidator(ILogger<Dnd5eEncounterValidator> logger)
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

        // Clamp inputs to valid ranges
        partySize = Math.Max(1, partySize);
        averageLevel = Math.Clamp(averageLevel, 1, 20);

        // Calculate total XP from all creatures
        var totalXp = 0;
        var totalMonsterCount = 0;
        var hasUnparsableCr = false;

        foreach (var creature in encounter.Enemies)
        {
            var count = Math.Max(1, creature.Count);
            totalMonsterCount += count;

            if (TryParseCrToXp(creature.ChallengeRating, out var xpPerCreature))
            {
                totalXp += xpPerCreature * count;
            }
            else
            {
                hasUnparsableCr = true;
                warnings.Add($"Could not parse CR '{creature.ChallengeRating}' for '{creature.Name}'. XP estimate may be inaccurate.");
            }
        }

        if (hasUnparsableCr && totalXp == 0)
        {
            // Cannot validate at all
            return new EncounterValidationResult
            {
                IsValid = true,
                EstimatedDifficulty = "unknown",
                Warnings = ["Unable to estimate difficulty: no creature CRs could be parsed."],
                TotalXP = 0,
                AdjustedXP = 0,
            };
        }

        // Apply encounter multiplier
        var multiplier = GetEncounterMultiplier(totalMonsterCount, partySize);
        var adjustedXp = (int)(totalXp * multiplier);

        // Get party thresholds
        var (easyThreshold, mediumThreshold, hardThreshold, deadlyThreshold) =
            GetPartyThresholds(partySize, averageLevel);

        // Determine estimated difficulty
        var estimatedDifficulty = DetermineCategory(adjustedXp, easyThreshold, mediumThreshold, hardThreshold, deadlyThreshold);

        // Flag mismatches between requested and estimated difficulty
        var requestedDifficulty = NormalizeDifficulty(encounter.Difficulty);
        var isValid = true;

        if (!string.IsNullOrEmpty(requestedDifficulty) && requestedDifficulty != "unknown")
        {
            var requestedRank = DifficultyRank(requestedDifficulty);
            var estimatedRank = DifficultyRank(estimatedDifficulty);
            var difference = estimatedRank - requestedRank;

            if (difference >= 2)
            {
                isValid = false;
                warnings.Add($"Encounter is significantly harder than requested. Requested '{requestedDifficulty}' but estimated '{estimatedDifficulty}' (adjusted XP: {adjustedXp}).");
            }
            else if (difference <= -2)
            {
                isValid = false;
                warnings.Add($"Encounter is significantly easier than requested. Requested '{requestedDifficulty}' but estimated '{estimatedDifficulty}' (adjusted XP: {adjustedXp}).");
            }
            else if (difference == 1)
            {
                warnings.Add($"Encounter is slightly harder than requested '{requestedDifficulty}'. Estimated as '{estimatedDifficulty}'.");
            }
            else if (difference == -1)
            {
                warnings.Add($"Encounter is slightly easier than requested '{requestedDifficulty}'. Estimated as '{estimatedDifficulty}'.");
            }
        }

        // Flag deadly encounters for small parties
        if (estimatedDifficulty == "deadly" && partySize <= 2)
        {
            warnings.Add("Deadly encounter for a small party (2 or fewer). Consider reducing difficulty.");
        }

        // Flag encounters with no enemies
        if (totalMonsterCount == 0)
        {
            isValid = false;
            warnings.Add("Encounter has no enemies.");
        }

        _logger.LogDebug(
            "Encounter '{Title}': {MonsterCount} monsters, {TotalXP} base XP, x{Multiplier} multiplier = {AdjustedXP} adjusted XP. " +
            "Party thresholds (E/M/H/D): {Easy}/{Medium}/{Hard}/{Deadly}. Estimated: {Difficulty}",
            encounter.Title, totalMonsterCount, totalXp, multiplier, adjustedXp,
            easyThreshold, mediumThreshold, hardThreshold, deadlyThreshold, estimatedDifficulty);

        return new EncounterValidationResult
        {
            IsValid = isValid,
            EstimatedDifficulty = estimatedDifficulty,
            Warnings = warnings,
            TotalXP = totalXp,
            AdjustedXP = adjustedXp,
        };
    }

    /// <summary>
    /// Attempts to parse a CR string to its XP value.
    /// Supports formats: "0", "1/8", "1/4", "1/2", "1", "2", ..., "30",
    /// as well as "CR 5", "cr 1/2", etc.
    /// </summary>
    public static bool TryParseCrToXp(string? cr, out int xp)
    {
        xp = 0;
        if (string.IsNullOrWhiteSpace(cr))
            return false;

        // Strip common prefixes like "CR " or "cr "
        var normalized = cr.Trim();
        if (normalized.StartsWith("CR ", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..].Trim();

        return CrToXpTable.TryGetValue(normalized, out xp);
    }

    /// <summary>
    /// Gets the encounter multiplier based on monster count and party size.
    /// For parties smaller than 3, the multiplier is shifted up one bracket.
    /// For parties larger than 5, the multiplier is shifted down one bracket.
    /// </summary>
    public static double GetEncounterMultiplier(int monsterCount, int partySize)
    {
        if (monsterCount <= 0)
            return 1.0;

        var multiplierIndex = 0;
        for (var i = 0; i < EncounterMultipliers.Length; i++)
        {
            if (monsterCount >= EncounterMultipliers[i].MinMonsters &&
                monsterCount <= EncounterMultipliers[i].MaxMonsters)
            {
                multiplierIndex = i;
                break;
            }
        }

        // Adjust for party size
        if (partySize < 3)
            multiplierIndex = Math.Min(multiplierIndex + 1, EncounterMultipliers.Length - 1);
        else if (partySize > 5)
            multiplierIndex = Math.Max(multiplierIndex - 1, 0);

        return EncounterMultipliers[multiplierIndex].Multiplier;
    }

    /// <summary>
    /// Gets the total party XP thresholds for each difficulty category.
    /// </summary>
    public static (int Easy, int Medium, int Hard, int Deadly) GetPartyThresholds(int partySize, int averageLevel)
    {
        var levelIndex = Math.Clamp(averageLevel - 1, 0, XpThresholdsByLevel.Length - 1);
        var perCharacter = XpThresholdsByLevel[levelIndex];

        return (
            perCharacter.Easy * partySize,
            perCharacter.Medium * partySize,
            perCharacter.Hard * partySize,
            perCharacter.Deadly * partySize
        );
    }

    private static string DetermineCategory(int adjustedXp, int easy, int medium, int hard, int deadly)
    {
        if (adjustedXp >= deadly)
            return "deadly";
        if (adjustedXp >= hard)
            return "hard";
        if (adjustedXp >= medium)
            return "medium";
        if (adjustedXp >= easy)
            return "easy";
        return "trivial";
    }

    private static string NormalizeDifficulty(string? difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
            return "unknown";

        return difficulty.Trim().ToLowerInvariant() switch
        {
            "easy" => "easy",
            "medium" => "medium",
            "hard" => "hard",
            "deadly" => "deadly",
            "trivial" => "trivial",
            _ => "unknown",
        };
    }

    private static int DifficultyRank(string difficulty) => difficulty switch
    {
        "trivial" => 0,
        "easy" => 1,
        "medium" => 2,
        "hard" => 3,
        "deadly" => 4,
        _ => 2, // Default to medium if unknown
    };
}
