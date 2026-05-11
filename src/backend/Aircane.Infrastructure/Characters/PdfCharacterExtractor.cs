using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;

namespace Aircane.Infrastructure.Characters;

/// <summary>
/// Extracts character data from a PDF using PdfPig.
/// Strategy:
///   1. Try AcroForm field extraction (form-fillable PDFs).
///   2. Fall back to text-layer extraction if no form fields are found.
///   3. Mark <see cref="PdfCharacterExtractionResult.IsOcrRequired"/> = true
///      when neither strategy yields any content.
/// </summary>
public sealed class PdfCharacterExtractor : IPdfCharacterExtractor
{
    private readonly ILogger<PdfCharacterExtractor> _logger;

    public PdfCharacterExtractor(ILogger<PdfCharacterExtractor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PdfCharacterExtractionResult> ExtractAsync(Stream pdfStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        // PdfPig is synchronous; wrap in Task.Run to avoid blocking the thread pool.
        return Task.Run(() => Extract(pdfStream), ct);
    }

    // ── Core extraction ───────────────────────────────────────────────────────

    private PdfCharacterExtractionResult Extract(Stream pdfStream)
    {
        var warnings = new List<string>();
        Dictionary<string, string> extractedFields;

        try
        {
            using var document = PdfDocument.Open(pdfStream);

            // 1. Try AcroForm fields first
            extractedFields = TryExtractFormFields(document, warnings);

            // 2. Fall back to text extraction if no form fields found
            if (extractedFields.Count == 0)
            {
                _logger.LogDebug("No AcroForm fields found; falling back to text extraction.");
                extractedFields = TryExtractTextFields(document, warnings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open or parse PDF for character extraction.");
            return new PdfCharacterExtractionResult
            {
                ExtractedFields = [],
                MappedCharacter = null,
                UnmappedFields = [],
                IsOcrRequired = true,
                Warnings = [$"Could not read PDF: {ex.Message}"]
            };
        }

        // 3. If still empty, OCR is required
        if (extractedFields.Count == 0)
        {
            _logger.LogInformation("PDF yielded no extractable text or form fields; OCR required.");
            return new PdfCharacterExtractionResult
            {
                ExtractedFields = [],
                MappedCharacter = null,
                UnmappedFields = [],
                IsOcrRequired = true,
                Warnings = ["No extractable text or form fields found. OCR is required to read this PDF."]
            };
        }

        // 4. Map extracted fields to canonical character
        var (mapped, unmapped) = MapToCanonical(extractedFields, warnings);

        _logger.LogInformation(
            "PDF character extraction complete: {FieldCount} fields extracted, {UnmappedCount} unmapped, {WarningCount} warnings.",
            extractedFields.Count, unmapped.Count, warnings.Count);

        return new PdfCharacterExtractionResult
        {
            ExtractedFields = extractedFields,
            MappedCharacter = mapped,
            UnmappedFields = unmapped,
            IsOcrRequired = false,
            Warnings = warnings
        };
    }

    // ── AcroForm field extraction ─────────────────────────────────────────────

    private static Dictionary<string, string> TryExtractFormFields(
        PdfDocument document,
        List<string> warnings)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!document.TryGetForm(out var form) || form is null)
                return fields;

            foreach (var field in form.Fields)
            {
                var name = (field.Information.MappingName ?? field.Information.PartialName)?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                string value = field switch
                {
                    AcroTextField textField => textField.Value ?? string.Empty,
                    AcroCheckboxField checkbox => checkbox.CurrentValue.Data ?? string.Empty,
                    AcroComboBoxField comboBox => string.Join(", ", comboBox.SelectedOptions),
                    AcroListBoxField listBox => string.Join(", ", listBox.SelectedOptions),
                    _ => string.Empty
                };

                // Last writer wins for duplicate field names
                fields[name] = value;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"AcroForm extraction warning: {ex.Message}");
        }

        return fields;
    }

    // ── Text-layer extraction ─────────────────────────────────────────────────

    /// <summary>
    /// Extracts text from all pages and attempts to parse "Label: Value" patterns.
    /// This is a best-effort heuristic for text-layer PDFs that are not form-fillable.
    /// </summary>
    private static Dictionary<string, string> TryExtractTextFields(
        PdfDocument document,
        List<string> warnings)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var text in document.GetPages().Select(p => p.Text))
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                ParseLabelValuePairs(text, fields);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Text extraction warning: {ex.Message}");
        }

        return fields;
    }

    /// <summary>
    /// Parses lines of the form "Label: Value" or "Label Value" into a dictionary.
    /// Handles common D&amp;D 5e character sheet text patterns.
    /// </summary>
    private static void ParseLabelValuePairs(string text, Dictionary<string, string> fields)
    {
        var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Try "Label: Value" pattern
            var colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
            {
                var key = trimmed[..colonIndex].Trim();
                var value = trimmed[(colonIndex + 1)..].Trim();

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    fields.TryAdd(key, value);
                }
            }
        }
    }

    // ── Field mapping ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maps extracted field dictionary to a <see cref="CanonicalCharacter"/>.
    /// Returns the mapped character and a dictionary of fields that could not be mapped.
    /// </summary>
    private static (CanonicalCharacter Mapped, Dictionary<string, string> Unmapped) MapToCanonical(
        Dictionary<string, string> fields,
        List<string> warnings)
    {
        var character = new CanonicalCharacter();
        var unmapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in fields)
        {
            if (!TryMapField(key, value, character, warnings))
            {
                unmapped[key] = value;
            }
        }

        return (character, unmapped);
    }

    /// <summary>
    /// Attempts to map a single field key/value to the canonical character.
    /// Returns <c>true</c> if the field was recognized and mapped (even if the value was invalid).
    /// Returns <c>false</c> if the field name is not recognized.
    /// </summary>
    private static bool TryMapField(
        string key,
        string value,
        CanonicalCharacter character,
        List<string> warnings)
    {
        // Normalize key for comparison
        var normalizedKey = key.Replace(" ", "").Replace("_", "").ToLowerInvariant();

        switch (normalizedKey)
        {
            // ── Identity ──────────────────────────────────────────────────────

            case "charactername":
            case "name":
                character.Identity.Name = value;
                return true;

            case "race":
            case "ancestry":
                character.Identity.RaceOrAncestry = value;
                return true;

            case "background":
                character.Identity.Background = value;
                return true;

            case "alignment":
                character.Identity.Alignment = value;
                return true;

            // ── Class / Level ─────────────────────────────────────────────────

            case "classlevel":
            case "class":
                MapClassLevel(value, character, warnings);
                return true;

            case "level":
                MapLevel(value, character, warnings);
                return true;

            // ── Ability scores ────────────────────────────────────────────────

            case "str":
            case "strength":
                character.Abilities.Strength = ParseAbilityScore(key, value, warnings);
                return true;

            case "dex":
            case "dexterity":
                character.Abilities.Dexterity = ParseAbilityScore(key, value, warnings);
                return true;

            case "con":
            case "constitution":
                character.Abilities.Constitution = ParseAbilityScore(key, value, warnings);
                return true;

            case "int":
            case "intelligence":
                character.Abilities.Intelligence = ParseAbilityScore(key, value, warnings);
                return true;

            case "wis":
            case "wisdom":
                character.Abilities.Wisdom = ParseAbilityScore(key, value, warnings);
                return true;

            case "cha":
            case "charisma":
                character.Abilities.Charisma = ParseAbilityScore(key, value, warnings);
                return true;

            // ── Combat ────────────────────────────────────────────────────────

            case "ac":
            case "armorclass":
                character.Combat.ArmorClass = ParseIntField(key, value, 10, warnings);
                return true;

            case "hpmax":
            case "maxhp":
            case "hp":
            case "hitpointmaximum":
                character.Combat.MaxHitPoints = ParseIntField(key, value, 0, warnings);
                return true;

            case "speed":
                character.Combat.Speed = ParseIntField(key, value, 30, warnings);
                return true;

            case "initiative":
                character.Combat.Initiative = ParseIntField(key, value, 0, warnings);
                return true;

            case "proficiencybonus":
                character.Combat.ProficiencyBonus = ParseIntField(key, value, 2, warnings);
                return true;

            // ── Saving throws ─────────────────────────────────────────────────

            case "savingthrowstr":
            case "strsave":
            case "strengthsave":
            case "strengthsavingthrow":
                character.SavingThrows.Strength = ParseBoolField(value);
                return true;

            case "savingthrowdex":
            case "dexsave":
            case "dexteritysave":
            case "dexteritysavingthrow":
                character.SavingThrows.Dexterity = ParseBoolField(value);
                return true;

            case "savingthrowcon":
            case "consave":
            case "constitutionsave":
            case "constitutionsavingthrow":
                character.SavingThrows.Constitution = ParseBoolField(value);
                return true;

            case "savingthrowint":
            case "intsave":
            case "intelligencesave":
            case "intelligencesavingthrow":
                character.SavingThrows.Intelligence = ParseBoolField(value);
                return true;

            case "savingthrowwis":
            case "wissave":
            case "wisdomsave":
            case "wisdomsavingthrow":
                character.SavingThrows.Wisdom = ParseBoolField(value);
                return true;

            case "savingthrowcha":
            case "chasave":
            case "charismasave":
            case "charismasavingthrow":
                character.SavingThrows.Charisma = ParseBoolField(value);
                return true;

            // ── Skills ────────────────────────────────────────────────────────

            case "acrobatics":
            case "animalhandling":
            case "arcana":
            case "athletics":
            case "deception":
            case "history":
            case "insight":
            case "intimidation":
            case "investigation":
            case "medicine":
            case "nature":
            case "perception":
            case "performance":
            case "persuasion":
            case "religion":
            case "sleightofhand":
            case "stealth":
            case "survival":
                MapSkillProficiency(key, value, character);
                return true;

            default:
                return false;
        }
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses a boolean/proficiency field.
    /// Recognises "Yes", "True", "1", "on", "✓", "X", "x" as true; everything else as false.
    /// </summary>
    private static bool ParseBoolField(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        return v is "yes" or "true" or "1" or "on" or "✓" or "x" or "checked";
    }

    /// <summary>
    /// Maps a skill field to a <see cref="SkillProficiency"/> entry on the character.
    /// The value is interpreted as a proficiency level: "E"/"Expert" → Expert,
    /// truthy → Proficient, falsy → None.
    /// </summary>
    private static void MapSkillProficiency(string key, string value, CanonicalCharacter character)
    {
        // Reconstruct the display name from the normalized key (best-effort title-case)
        var skillName = ToTitleCase(key);

        var level = value.Trim().ToLowerInvariant() switch
        {
            "e" or "expert" or "expertise" or "2" => ProficiencyLevel.Expert,
            "h" or "half" or "halfproficient" or "0.5" => ProficiencyLevel.HalfProficient,
            var v when ParseBoolField(v) => ProficiencyLevel.Proficient,
            _ => ProficiencyLevel.None
        };

        // Update existing entry or add new one
        var existing = character.Skills.FirstOrDefault(s =>
            string.Equals(s.SkillName, skillName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            existing.ProficiencyLevel = level;
        else
            character.Skills.Add(new SkillProficiency { SkillName = skillName, ProficiencyLevel = level });
    }

    /// <summary>
    /// Converts a camelCase or lowercase identifier to a display-friendly title-case string.
    /// e.g. "sleightofhand" → "Sleight Of Hand" (best-effort; known names are mapped explicitly).
    /// </summary>
    private static string ToTitleCase(string key) =>
        key.Trim().ToLowerInvariant() switch
        {
            "acrobatics" => "Acrobatics",
            "animalhandling" => "Animal Handling",
            "arcana" => "Arcana",
            "athletics" => "Athletics",
            "deception" => "Deception",
            "history" => "History",
            "insight" => "Insight",
            "intimidation" => "Intimidation",
            "investigation" => "Investigation",
            "medicine" => "Medicine",
            "nature" => "Nature",
            "perception" => "Perception",
            "performance" => "Performance",
            "persuasion" => "Persuasion",
            "religion" => "Religion",
            "sleightofhand" => "Sleight of Hand",
            "stealth" => "Stealth",
            "survival" => "Survival",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key)
        };

    /// <summary>
    /// Parses an ability score value. Adds a warning and returns the default (10) on failure.
    /// </summary>
    private static int ParseAbilityScore(string fieldName, string value, List<string> warnings)
    {
        // Strip modifier suffix like "16 (+3)" → "16"
        var cleaned = value.Split([' ', '('])[0].Trim();

        if (int.TryParse(cleaned, out var score))
            return score;

        warnings.Add($"'{fieldName}' value '{value}' is not a valid integer — defaulting to 10.");
        return 10;
    }

    /// <summary>
    /// Parses an integer field. Adds a warning and returns <paramref name="defaultValue"/> on failure.
    /// </summary>
    private static int ParseIntField(string fieldName, string value, int defaultValue, List<string> warnings)
    {
        // Strip non-numeric suffix, e.g. "30 ft." → "30"
        var cleaned = value.Split(' ')[0].Trim().TrimEnd('f', 't', '.');

        if (int.TryParse(cleaned, out var result))
            return result;

        warnings.Add($"'{fieldName}' value '{value}' is not a valid integer — defaulting to {defaultValue}.");
        return defaultValue;
    }

    /// <summary>
    /// Maps a combined "Class Level" field such as "Fighter 5" or "Wizard 3 / Rogue 2".
    /// Populates <see cref="CanonicalCharacter.Classes"/>.
    /// </summary>
    private static void MapClassLevel(string value, CanonicalCharacter character, List<string> warnings)
    {
        // Handle multiclass notation: "Fighter 5 / Rogue 3"
        var entries = value.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var parts = entry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[^1], out var level))
            {
                var className = string.Join(" ", parts[..^1]);
                character.Classes.Add(new CharacterClass
                {
                    ClassName = className,
                    Level = level,
                    HitDie = DefaultHitDieForClass(className)
                });
            }
            else if (parts.Length == 1)
            {
                // Class name only, no level — add with level 1 as a placeholder
                character.Classes.Add(new CharacterClass
                {
                    ClassName = parts[0],
                    Level = 1,
                    HitDie = DefaultHitDieForClass(parts[0])
                });
                warnings.Add($"'ClassLevel' value '{value}' did not include a level — defaulting to 1.");
            }
            else
            {
                warnings.Add($"'ClassLevel' value '{value}' could not be parsed.");
            }
        }
    }

    /// <summary>
    /// Maps a standalone "Level" field to the first class entry, or creates a placeholder class.
    /// </summary>
    private static void MapLevel(string value, CanonicalCharacter character, List<string> warnings)
    {
        if (!int.TryParse(value.Trim(), out var level))
        {
            warnings.Add($"'Level' value '{value}' is not a valid integer — ignoring.");
            return;
        }

        if (character.Classes.Count > 0)
        {
            character.Classes[0].Level = level;
        }
        else
        {
            // No class yet — create a placeholder
            character.Classes.Add(new CharacterClass
            {
                ClassName = "Unknown",
                Level = level,
                HitDie = 8
            });
        }
    }

    /// <summary>
    /// Returns a sensible default hit die for well-known D&amp;D 5e class names.
    /// Falls back to d8 for unknown classes.
    /// </summary>
    private static int DefaultHitDieForClass(string className) =>
        className.Trim().ToLowerInvariant() switch
        {
            "barbarian" => 12,
            "fighter" or "paladin" or "ranger" => 10,
            "bard" or "cleric" or "druid" or "monk" or "rogue" or "warlock" => 8,
            "sorcerer" or "wizard" => 6,
            _ => 8
        };
}
