using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Aircane.Application.Characters.Import;
using Aircane.Application.DocumentProcessing;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Content;

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
    private readonly ICaptionRegionOcr? _captionOcr;
    private readonly OcrOptions _ocrOptions;

    public PdfCharacterExtractor(
        ILogger<PdfCharacterExtractor> logger,
        ICaptionRegionOcr? captionOcr = null,
        OcrOptions? ocrOptions = null)
    {
        _logger = logger;
        _captionOcr = captionOcr;
        _ocrOptions = ocrOptions ?? new OcrOptions();
    }

    /// <inheritdoc />
    public Task<PdfCharacterExtractionResult> ExtractAsync(Stream pdfStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        // PdfPig is synchronous; wrap in Task.Run to avoid blocking the thread pool.
        return Task.Run(() => Extract(pdfStream, ct), ct);
    }

    // ── Core extraction ───────────────────────────────────────────────────────

    private PdfCharacterExtractionResult Extract(Stream pdfStream, CancellationToken ct)
    {
        var warnings = new List<string>();
        Dictionary<string, string> extractedFields;

        // Buffer the forward-only stream into a byte[] ONCE: PdfPig consumes it for caption geometry
        // and the same buffer is reused for rasterization on the OCR branch (finding 5).
        byte[] pdfBytes;
        using (var buffer = new MemoryStream())
        {
            pdfStream.CopyTo(buffer);
            pdfBytes = buffer.ToArray();
        }

        DndBeyondSignatureResult signature = default;
        IReadOnlyDictionary<string, CaptionBox>? captionBoxes = null;

        try
        {
            using var parseStream = new MemoryStream(pdfBytes, writable: false);
            using var document = PdfDocument.Open(parseStream);

            // 1. Try AcroForm fields first
            extractedFields = TryExtractFormFields(document, warnings);

            // 2. Fall back to text extraction if no form fields found
            if (extractedFields.Count == 0)
            {
                _logger.LogDebug("No AcroForm fields found; falling back to text extraction.");
                extractedFields = TryExtractTextFields(document, warnings);
            }

            // DDB signature gate: compute the text-layer caption word set and caption geometry so a
            // DDB printable sheet forces the OCR route even when it emitted no mappable fields.
            (signature, captionBoxes) = DetectDndBeyond(document);
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

        // 3. Routing: a DDB signature match OR an empty text layer takes the OCR branch when the OCR
        //    stack is available. The signature gate runs BEFORE the field-count decision so a future
        //    caption-mappable DDB sheet is not short-circuited.
        var forceOcr = signature.IsMatch;
        if (forceOcr || extractedFields.Count == 0)
        {
            if (_captionOcr is { IsAvailable: true } && captionBoxes is not null)
            {
                return RunOcrBranch(pdfBytes, captionBoxes, signature.Ruleset, ct);
            }

            if (forceOcr)
            {
                // Detected a DDB sheet but OCR is unavailable → actionable 422 upstream.
                _logger.LogInformation("DDB printable sheet detected but OCR is unavailable; flagging OCR-unavailable.");
                return new PdfCharacterExtractionResult
                {
                    ExtractedFields = extractedFields,
                    MappedCharacter = null,
                    UnmappedFields = [],
                    IsOcrRequired = true,
                    OcrUnavailableForDdb = true,
                    DetectedRuleset = signature.Ruleset,
                    Warnings = ["A D&D Beyond character sheet was detected, but OCR is required to read its values and is not available."]
                };
            }

            // 3b. Truly empty text layer and not a DDB sheet: OCR required (generic).
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

    // ── DDB OCR branch ──────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the DDB OCR route: builds value regions from caption geometry (page 1), OCRs them via
    /// the <see cref="ICaptionRegionOcr"/> seam, and maps the regions to a canonical character via
    /// <see cref="DndBeyondOcrMapper"/>. Never throws (helper gates/returns empty on failure); a
    /// region that fails OCR is left at its canonical default and flagged by the mapper.
    /// </summary>
    private PdfCharacterExtractionResult RunOcrBranch(
        byte[] pdfBytes,
        IReadOnlyDictionary<string, CaptionBox> captionBoxes,
        DdbRuleset ruleset,
        CancellationToken ct)
    {
        var regions = DndBeyondSheetLayout.BuildRegions(captionBoxes);

        // ICaptionRegionOcr is async; this method runs inside Task.Run so a synchronous wait is safe.
        var ocrRegions = _captionOcr!
            .RecognizeRegionsAsync(pdfBytes, pageNumber: 1, regions, ct)
            .GetAwaiter()
            .GetResult();

        var result = DndBeyondOcrMapper.Map(ocrRegions, ruleset);

        _logger.LogInformation(
            "DDB OCR extraction complete: {RegionCount} regions OCR'd, {ReviewCount} fields flagged for review, ruleset {Ruleset}.",
            ocrRegions.Count, result.RequiresReviewPaths.Count, ruleset);

        return result;
    }

    /// <summary>
    /// Detects the DDB printable-sheet signature from page-1 caption words and returns the normalized
    /// caption bounding boxes (TOP-DOWN, normalized to page dims) keyed by the verified caption token,
    /// for layout anchoring. The HP captions are disambiguated by position (the uppermost HP caption
    /// is treated as the max-HP anchor).
    /// </summary>
    private static (DndBeyondSignatureResult Signature, IReadOnlyDictionary<string, CaptionBox> Boxes) DetectDndBeyond(
        PdfDocument document)
    {
        var captionTokens = new List<string>();
        var boxes = new Dictionary<string, CaptionBox>(StringComparer.OrdinalIgnoreCase);

        Page? firstPage = null;
        foreach (var page in document.GetPages())
        {
            firstPage = page;
            break;
        }

        if (firstPage is null)
            return (default, boxes);

        var pageWidth = firstPage.Width;
        var pageHeight = firstPage.Height;
        if (pageWidth <= 0 || pageHeight <= 0)
            return (default, boxes);

        var words = firstPage.GetWords().ToList();

        // Reconstruct multi-word captions (e.g. "CLASS & LEVEL", "HIT POINTS") by grouping words on a
        // line (Y within 3pt) and scanning for the known caption phrases, plus single-word captions.
        var lines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList())
            .ToList();

        foreach (var line in lines)
        {
            foreach (var caption in KnownCaptions)
            {
                if (TryMatchCaptionOnLine(line, caption, out var box, pageWidth, pageHeight))
                {
                    captionTokens.Add(caption);
                    AddCaptionBox(boxes, caption, box);
                }
            }
        }

        var signature = DndBeyondSheetSignature.Detect(captionTokens);
        return (signature, boxes);
    }

    /// <summary>
    /// Records a caption box, disambiguating duplicate captions by position: for HP the UPPERMOST
    /// occurrence (smaller top-down Y) is kept as the max-HP anchor; for any other duplicate the
    /// first occurrence wins.
    /// </summary>
    private static void AddCaptionBox(Dictionary<string, CaptionBox> boxes, string caption, CaptionBox box)
    {
        if (!boxes.TryGetValue(caption, out var existing))
        {
            boxes[caption] = box;
            return;
        }

        // HIT POINTS: keep the uppermost (smallest Y, top-down) occurrence.
        if (string.Equals(caption, DndBeyondPdfHints.HitPointsCaption, StringComparison.OrdinalIgnoreCase) &&
            box.Y < existing.Y)
        {
            boxes[caption] = box;
        }
        // else: keep the first occurrence.
    }

    /// <summary>
    /// The caption phrases recognised on the text layer. These are template text (not PII, NFR-4).
    /// Multi-word phrases are matched as consecutive words on a line.
    /// </summary>
    private static readonly string[] KnownCaptions =
    [
        DndBeyondPdfHints.StrengthCaption,
        DndBeyondPdfHints.DexterityCaption,
        DndBeyondPdfHints.ConstitutionCaption,
        DndBeyondPdfHints.IntelligenceCaption,
        DndBeyondPdfHints.WisdomCaption,
        DndBeyondPdfHints.CharismaCaption,
        DndBeyondPdfHints.CharacterNameCaption,
        DndBeyondPdfHints.ClassLevelCaption,
        DndBeyondPdfHints.ArmorCaption,
        DndBeyondPdfHints.ArmorClassAliasCaption,
        DndBeyondPdfHints.PassivePerceptionCaption,
        DndBeyondPdfHints.ProficiencyBonusCaption,
        DndBeyondPdfHints.HitPointsCaption,
        DndBeyondPdfHints.SpeedCaption,
        DndBeyondPdfHints.SpeciesCaption,
        DndBeyondPdfHints.RaceCaption,
        DndBeyondPdfHints.BackgroundCaption,
    ];

    /// <summary>
    /// Attempts to match a caption (possibly multi-word) as a run of consecutive words on a line.
    /// On success emits the union bounding box normalized to TOP-DOWN page coordinates.
    /// </summary>
    private static bool TryMatchCaptionOnLine(
        List<Word> line,
        string caption,
        out CaptionBox box,
        double pageWidth,
        double pageHeight)
    {
        box = default;

        var captionWords = caption.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var start = 0; start + captionWords.Length <= line.Count; start++)
        {
            var matched = true;
            for (var i = 0; i < captionWords.Length; i++)
            {
                if (!string.Equals(line[start + i].Text.Trim(), captionWords[i], StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
                continue;

            // Union of the matched words' PDF-coordinate boxes (origin bottom-left, Y upward).
            var first = line[start].BoundingBox;
            var last = line[start + captionWords.Length - 1].BoundingBox;

            var left = Math.Min(first.Left, last.Left);
            var right = Math.Max(first.Right, last.Right);
            var top = Math.Max(first.Top, last.Top);       // higher PDF Y = nearer page top
            var bottom = Math.Min(first.Bottom, last.Bottom);

            // Convert PDF (bottom-up) to normalized TOP-DOWN: topY = (pageHeight - pdfTop)/pageHeight.
            var nx = left / pageWidth;
            var ny = (pageHeight - top) / pageHeight;
            var nw = (right - left) / pageWidth;
            var nh = (top - bottom) / pageHeight;

            box = new CaptionBox(nx, ny, nw, nh);
            return true;
        }

        return false;
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
    /// Uses word-level extraction with Y-position grouping for consistent cross-platform behavior.
    /// </summary>
    private static Dictionary<string, string> TryExtractTextFields(
        PdfDocument document,
        List<string> warnings)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var page in document.GetPages())
            {
                var words = page.GetWords().ToList();
                if (words.Count == 0)
                    continue;

                // Group words into lines by Y-position (words within 3pt of each other are on the same line)
                var lines = words
                    .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
                    .OrderByDescending(g => g.Key) // top of page first
                    .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
                    .ToList();

                var text = string.Join("\n", lines);
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
            // D&D Beyond PDF form-field names take priority over the generic heuristics: when a
            // field name matches a known DDB hint, map it via the hinted canonical path.
            if (TryMapByDndBeyondHint(key, value, character, warnings))
                continue;

            if (!TryMapField(key, value, character, warnings))
            {
                unmapped[key] = value;
            }
        }

        return (character, unmapped);
    }

    /// <summary>
    /// Priority override for D&amp;D Beyond PDF exports: when a form-field name matches a key in
    /// <see cref="DndBeyondPdfHints.FieldMap"/>, apply the value via the hinted canonical path. The
    /// combined <c>ClassLevel</c> field is split into class name + level. Returns <c>true</c> when
    /// the field was recognised as a DDB hint (and handled), <c>false</c> otherwise.
    /// </summary>
    private static bool TryMapByDndBeyondHint(
        string key,
        string value,
        CanonicalCharacter character,
        List<string> warnings)
    {
        var trimmedKey = key.Trim();

        // ClassLevel is a combined "class name + level" field; split via the ONE shared split helper
        // (also used by the DDB OCR mapper) so there is a single implementation.
        if (string.Equals(trimmedKey, DndBeyondPdfHints.ClassLevelFieldName, StringComparison.OrdinalIgnoreCase))
        {
            DndBeyondClassLevelSplit.Apply(value, character, warnings);
            return true;
        }

        if (!DndBeyondPdfHints.FieldMap.TryGetValue(trimmedKey, out var canonicalPath))
            return false;

        ApplyCanonicalPath(canonicalPath, trimmedKey, value, character, warnings);
        return true;
    }

    /// <summary>Applies a value to the character for a known canonical <c>ApplyMapping</c> path.</summary>
    private static void ApplyCanonicalPath(
        string canonicalPath,
        string fieldName,
        string value,
        CanonicalCharacter character,
        List<string> warnings)
    {
        switch (canonicalPath)
        {
            case CanonicalCharacterPaths.IdentityName:
                character.Identity.Name = value;
                break;
            case CanonicalCharacterPaths.IdentityRaceOrAncestry:
                character.Identity.RaceOrAncestry = value;
                break;
            case CanonicalCharacterPaths.IdentityBackground:
                character.Identity.Background = value;
                break;
            case CanonicalCharacterPaths.AbilityStrength:
                character.Abilities.Strength = ParseAbilityScore(fieldName, value, warnings);
                break;
            case CanonicalCharacterPaths.AbilityDexterity:
                character.Abilities.Dexterity = ParseAbilityScore(fieldName, value, warnings);
                break;
            case CanonicalCharacterPaths.AbilityConstitution:
                character.Abilities.Constitution = ParseAbilityScore(fieldName, value, warnings);
                break;
            case CanonicalCharacterPaths.AbilityIntelligence:
                character.Abilities.Intelligence = ParseAbilityScore(fieldName, value, warnings);
                break;
            case CanonicalCharacterPaths.AbilityWisdom:
                character.Abilities.Wisdom = ParseAbilityScore(fieldName, value, warnings);
                break;
            case CanonicalCharacterPaths.AbilityCharisma:
                character.Abilities.Charisma = ParseAbilityScore(fieldName, value, warnings);
                break;
            case CanonicalCharacterPaths.CombatMaxHitPoints:
                character.Combat.MaxHitPoints = ParseIntField(fieldName, value, 0, warnings);
                break;
            case CanonicalCharacterPaths.CombatHitPoints:
                character.Combat.CurrentHitPoints = ParseIntField(fieldName, value, 0, warnings);
                break;
            case CanonicalCharacterPaths.CombatArmorClass:
                character.Combat.ArmorClass = ParseIntField(fieldName, value, 10, warnings);
                break;
            case CanonicalCharacterPaths.CombatSpeed:
                character.Combat.Speed = ParseIntField(fieldName, value, 30, warnings);
                break;
            case CanonicalCharacterPaths.CombatProficiencyBonus:
                character.Combat.ProficiencyBonus = ParseIntField(fieldName, value, 2, warnings);
                break;
        }
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

        warnings.Add($"'{fieldName}' value '{value}' is not a valid integer - defaulting to 10.");
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

        warnings.Add($"'{fieldName}' value '{value}' is not a valid integer - defaulting to {defaultValue}.");
        return defaultValue;
    }

    /// <summary>
    /// Maps a combined "Class Level" field such as "Fighter 5" or "Wizard 3 / Rogue 2" via the ONE
    /// shared split helper (<see cref="DndBeyondClassLevelSplit"/>). Populates
    /// <see cref="CanonicalCharacter.Classes"/>.
    /// </summary>
    private static void MapClassLevel(string value, CanonicalCharacter character, List<string> warnings)
        => DndBeyondClassLevelSplit.Apply(value, character, warnings);

    /// <summary>
    /// Maps a standalone "Level" field to the first class entry, or creates a placeholder class.
    /// </summary>
    private static void MapLevel(string value, CanonicalCharacter character, List<string> warnings)
    {
        if (!int.TryParse(value.Trim(), out var level))
        {
            warnings.Add($"'Level' value '{value}' is not a valid integer - ignoring.");
            return;
        }

        if (character.Classes.Count > 0)
        {
            character.Classes[0].Level = level;
        }
        else
        {
            // No class yet - create a placeholder
            character.Classes.Add(new CharacterClass
            {
                ClassName = "Unknown",
                Level = level,
                HitDie = 8
            });
        }
    }

    /// <summary>
    /// Returns a sensible default hit die for well-known D&amp;D 5e class names (d8 fallback).
    /// Delegates to the ONE shared implementation in <see cref="DndBeyondClassLevelSplit"/>.
    /// </summary>
    private static int DefaultHitDieForClass(string className)
        => DndBeyondClassLevelSplit.DefaultHitDieForClass(className);
}
