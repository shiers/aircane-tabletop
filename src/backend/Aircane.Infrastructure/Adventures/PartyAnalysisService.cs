using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Analyzes a party of characters by loading their canonical JSON and extracting
/// capabilities, strengths, and weaknesses for adventure generation.
/// </summary>
public sealed class PartyAnalysisService : IPartyAnalysisService
{
    private readonly AircaneDbContext _db;
    private readonly ILogger<PartyAnalysisService> _logger;

    /// <summary>
    /// Spell names (lowercase) that are considered healing spells.
    /// </summary>
    private static readonly HashSet<string> HealingSpellNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cure wounds",
        "healing word",
        "mass cure wounds",
        "mass healing word",
        "heal",
        "prayer of healing",
        "goodberry",
        "lay on hands",
        "aura of vitality",
        "beacon of hope",
        "regenerate",
        "power word heal",
        "spare the dying",
        "life transference",
        "word of recall",
        "heroes' feast",
    };

    /// <summary>
    /// Feature names (lowercase) that indicate healing capability.
    /// </summary>
    private static readonly HashSet<string> HealingFeatureNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "lay on hands",
        "healing light",
        "balm of the summer court",
        "circle of mortality",
        "preserve life",
        "disciple of life",
        "song of rest",
    };

    public PartyAnalysisService(AircaneDbContext db, ILogger<PartyAnalysisService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PartyAnalysisResult> AnalyzePartyAsync(
        IReadOnlyList<Guid> characterIds,
        CancellationToken cancellationToken = default)
    {
        if (characterIds is null || characterIds.Count == 0)
            throw new ArgumentException("At least one character ID is required.", nameof(characterIds));

        // Load characters from the database
        var characters = await _db.Characters
            .AsNoTracking()
            .Where(c => characterIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (characters.Count == 0)
            throw new ArgumentException("No characters found for the provided IDs.", nameof(characterIds));

        _logger.LogInformation(
            "Analyzing party of {Count} character(s) (requested {Requested})",
            characters.Count, characterIds.Count);

        // Parse canonical JSON for each character
        var parsed = characters
            .Select(c => CharacterJsonSerializer.DeserializeOrDefault(c.CanonicalJson))
            .ToList();

        // Compute analysis
        var partySize = parsed.Count;
        var levels = parsed.Select(c => Math.Max(1, c.Classes.Sum(cl => cl.Level))).ToList();
        var averageLevel = levels.Average();
        var minLevel = levels.Min();
        var maxLevel = levels.Max();

        // Class distribution
        var classes = parsed
            .SelectMany(c => c.Classes)
            .Where(cl => !string.IsNullOrWhiteSpace(cl.ClassName))
            .GroupBy(cl => cl.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Combat stats
        var armorClasses = parsed.Select(c => c.Combat.ArmorClass).ToList();
        var averageAC = armorClasses.Average();

        var hitPoints = parsed.Select(c => Math.Max(c.Combat.MaxHitPoints, c.Combat.CurrentHitPoints)).ToList();
        var totalHP = hitPoints.Sum();
        var averageHP = hitPoints.Average();

        // Capability checks
        var hasHealing = parsed.Any(HasHealingCapability);
        var hasRangedAttacks = parsed.Any(HasRangedAttackCapability);
        var hasMagic = parsed.Any(HasMagicCapability);
        var hasStealth = parsed.Any(c => HasSkillProficiency(c, "stealth"));
        var hasPerception = parsed.Any(c => HasSkillProficiency(c, "perception"));

        // Build capabilities and weaknesses lists
        var capabilities = BuildCapabilities(parsed, classes, hasHealing, hasRangedAttacks, hasMagic, hasStealth, hasPerception);
        var weaknesses = BuildWeaknesses(parsed, classes, hasHealing, hasRangedAttacks, hasMagic, hasStealth, hasPerception);

        return new PartyAnalysisResult
        {
            PartySize = partySize,
            AverageLevel = Math.Round(averageLevel, 1),
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            Classes = classes,
            AverageAC = Math.Round(averageAC, 1),
            AverageHP = Math.Round(averageHP, 1),
            TotalHP = totalHP,
            HasHealing = hasHealing,
            HasRangedAttacks = hasRangedAttacks,
            HasMagic = hasMagic,
            HasStealth = hasStealth,
            HasPerception = hasPerception,
            Capabilities = capabilities,
            Weaknesses = weaknesses,
        };
    }

    // ── Capability Detection ──────────────────────────────────────────────────

    public static bool HasHealingCapability(CanonicalCharacter character)
    {
        // Check spells
        if (character.Spells?.KnownSpells.Any(s =>
            HealingSpellNames.Contains(s.Name)) == true)
            return true;

        // Check features
        if (character.Features.Any(f =>
            HealingFeatureNames.Contains(f.Name)))
            return true;

        // Check resources (e.g., "Lay on Hands" pool)
        if (character.Resources.Any(r =>
            HealingFeatureNames.Contains(r.Name)))
            return true;

        return false;
    }

    public static bool HasRangedAttackCapability(CanonicalCharacter character)
    {
        // Check attacks with range > 5 ft
        if (character.Attacks.Any(a =>
            !string.IsNullOrWhiteSpace(a.Range) &&
            !a.Range.Equals("5 ft.", StringComparison.OrdinalIgnoreCase) &&
            !a.Range.Equals("5 ft", StringComparison.OrdinalIgnoreCase) &&
            !a.Range.Equals("5ft", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check for ranged cantrips or attack spells
        if (character.Spells?.KnownSpells.Any(s =>
            s.Level == 0 && !string.IsNullOrWhiteSpace(s.Range) &&
            !s.Range.Equals("Touch", StringComparison.OrdinalIgnoreCase) &&
            !s.Range.Equals("Self", StringComparison.OrdinalIgnoreCase)) == true)
            return true;

        // Check attack properties for "Thrown" or "Ammunition"
        if (character.Attacks.Any(a =>
            a.Properties.Any(p =>
                p.Contains("thrown", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("ammunition", StringComparison.OrdinalIgnoreCase))))
            return true;

        return false;
    }

    public static bool HasMagicCapability(CanonicalCharacter character)
    {
        // Has spellcasting info with known spells
        if (character.Spells?.KnownSpells.Count > 0)
            return true;

        // Has spell slots
        if (character.Spells?.SpellSlots.Any(s => s.Total > 0) == true)
            return true;

        return false;
    }

    public static bool HasSkillProficiency(CanonicalCharacter character, string skillName)
    {
        return character.Skills.Any(s =>
            s.SkillName.Equals(skillName, StringComparison.OrdinalIgnoreCase) &&
            s.ProficiencyLevel >= ProficiencyLevel.Proficient);
    }

    // ── Capability / Weakness Summaries ───────────────────────────────────────

    private static IReadOnlyList<string> BuildCapabilities(
        List<CanonicalCharacter> party,
        Dictionary<string, int> classes,
        bool hasHealing,
        bool hasRangedAttacks,
        bool hasMagic,
        bool hasStealth,
        bool hasPerception)
    {
        var capabilities = new List<string>();

        if (hasHealing)
            capabilities.Add("Party has healing capability");

        if (hasRangedAttacks)
            capabilities.Add("Party has ranged attack options");

        if (hasMagic)
            capabilities.Add("Party has spellcasting");

        if (hasStealth)
            capabilities.Add("Party can attempt stealth approaches");

        if (hasPerception)
            capabilities.Add("Party has strong perception for detecting threats");

        // Check for tank/frontline
        var highAC = party.Count(c => c.Combat.ArmorClass >= 16);
        if (highAC > 0)
            capabilities.Add($"Party has {highAC} frontline character(s) with AC 16+");

        // Check for utility skills
        var hasInvestigation = party.Any(c => HasSkillProficiency(c, "investigation"));
        if (hasInvestigation)
            capabilities.Add("Party has investigation proficiency");

        var hasPersuasion = party.Any(c => HasSkillProficiency(c, "persuasion"));
        if (hasPersuasion)
            capabilities.Add("Party has social skills (persuasion)");

        var hasAthletics = party.Any(c => HasSkillProficiency(c, "athletics"));
        if (hasAthletics)
            capabilities.Add("Party has athletics proficiency");

        // Class-based capabilities
        if (classes.ContainsKey("Rogue") || classes.ContainsKey("rogue"))
            capabilities.Add("Party has rogue capabilities (traps, locks, sneak attack)");

        return capabilities;
    }

    private static IReadOnlyList<string> BuildWeaknesses(
        List<CanonicalCharacter> party,
        Dictionary<string, int> classes,
        bool hasHealing,
        bool hasRangedAttacks,
        bool hasMagic,
        bool hasStealth,
        bool hasPerception)
    {
        var weaknesses = new List<string>();

        if (!hasHealing)
            weaknesses.Add("No healing capability - consider recovery opportunities or reduced attrition");

        if (!hasRangedAttacks)
            weaknesses.Add("No ranged attacks - flying or distant enemies will be problematic");

        if (!hasMagic)
            weaknesses.Add("No spellcasting - magical barriers and resistances may block progress");

        if (!hasStealth)
            weaknesses.Add("No stealth proficiency - surprise approaches will be difficult");

        if (!hasPerception)
            weaknesses.Add("No perception proficiency - hidden threats may go unnoticed");

        // Check for low AC party
        var averageAC = party.Average(c => c.Combat.ArmorClass);
        if (averageAC < 14)
            weaknesses.Add("Low average AC - party is vulnerable to sustained attacks");

        // Check for low HP
        var averageHP = party.Average(c => Math.Max(c.Combat.MaxHitPoints, c.Combat.CurrentHitPoints));
        if (averageHP < 20 && party.Count > 0)
            weaknesses.Add("Low average HP - party cannot sustain prolonged combat");

        // Check for wisdom saves (common in D&D 5e)
        var hasWisdomSave = party.Any(c => c.SavingThrows.Wisdom);
        if (!hasWisdomSave)
            weaknesses.Add("No Wisdom save proficiency - vulnerable to charm and fear effects");

        // Check for class diversity
        if (classes.Count == 1 && party.Count > 1)
            weaknesses.Add("Single-class party - limited versatility in problem-solving approaches");

        return weaknesses;
    }
}
