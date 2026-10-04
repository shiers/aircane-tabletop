namespace Aircane.Application.Characters.Import;

/// <summary>
/// The D&amp;D Beyond ruleset a printable sheet was exported under, inferred from its ancestry
/// caption. <see cref="Unknown"/> means neither <c>SPECIES</c> nor <c>RACE</c> was present, so the
/// caller's supplied ruleset should be kept and flagged for confirmation.
/// </summary>
public enum DdbRuleset
{
    /// <summary>Neither SPECIES nor RACE detected; keep the caller/form ruleset, flag for review.</summary>
    Unknown = 0,

    /// <summary>The 2014 ("5e 2014") sheet — uses the <c>RACE</c> caption.</summary>
    Dnd2014,

    /// <summary>The 2024 ("5e 2024") sheet — uses the <c>SPECIES</c> caption.</summary>
    Dnd2024,
}

/// <summary>The outcome of <see cref="DndBeyondSheetSignature.Detect"/>.</summary>
/// <param name="IsMatch">True when the caption quorum identifies a DDB printable sheet.</param>
/// <param name="Ruleset">The ruleset inferred from the ancestry caption (SPECIES ⇒ 2024, RACE ⇒ 2014).</param>
public readonly record struct DndBeyondSignatureResult(bool IsMatch, DdbRuleset Ruleset);

/// <summary>
/// Detects whether a PDF's text-layer caption set belongs to a D&amp;D Beyond printable character
/// sheet. The share-link export carries only template captions as bare words (no AcroForm, no
/// "Label: Value" pairs — see FEAT-002-tuning-note.md), so the signature is a quorum over those
/// captions. Caption tokens come from <see cref="DndBeyondPdfHints"/> so there is one vocabulary
/// shared with the layout and the OCR mapper.
/// </summary>
public static class DndBeyondSheetSignature
{
    private static readonly string[] AbilityCaptions =
    [
        DndBeyondPdfHints.StrengthCaption,
        DndBeyondPdfHints.DexterityCaption,
        DndBeyondPdfHints.ConstitutionCaption,
        DndBeyondPdfHints.IntelligenceCaption,
        DndBeyondPdfHints.WisdomCaption,
        DndBeyondPdfHints.CharismaCaption,
    ];

    private static readonly string[] CorroboratingCaptions =
    [
        DndBeyondPdfHints.CharacterNameCaption,
        DndBeyondPdfHints.ClassLevelCaption,
        DndBeyondPdfHints.ArmorCaption,
        DndBeyondPdfHints.PassivePerceptionCaption,
    ];

    /// <summary>
    /// Returns a positive match when the normalized caption set contains ALL six ability captions
    /// (STRENGTH…CHARISMA) AND at least two of
    /// { CHARACTER NAME, CLASS &amp; LEVEL, ARMOR, PASSIVE PERCEPTION }. The quorum (not an exact
    /// set) tolerates minor template revisions while avoiding false positives on arbitrary PDFs.
    /// <para>
    /// Ruleset: <c>SPECIES</c> ⇒ <see cref="DdbRuleset.Dnd2024"/>; <c>RACE</c> (and not SPECIES) ⇒
    /// <see cref="DdbRuleset.Dnd2014"/>; neither ⇒ <see cref="DdbRuleset.Unknown"/> (caller default,
    /// flagged for confirmation). Ruleset is reported even on a non-match for diagnostic symmetry.
    /// </para>
    /// Captions are matched case-insensitively against the uppercased/trimmed tokens.
    /// </summary>
    public static DndBeyondSignatureResult Detect(IReadOnlyCollection<string> captions)
    {
        var normalized = Normalize(captions);

        var hasAllAbilities = AbilityCaptions.All(normalized.Contains);
        var corroborating = CorroboratingCaptions.Count(normalized.Contains);
        var isMatch = hasAllAbilities && corroborating >= 2;

        var ruleset = DetectRuleset(normalized);

        return new DndBeyondSignatureResult(isMatch, ruleset);
    }

    private static DdbRuleset DetectRuleset(HashSet<string> normalized)
    {
        if (normalized.Contains(DndBeyondPdfHints.SpeciesCaption))
            return DdbRuleset.Dnd2024;
        if (normalized.Contains(DndBeyondPdfHints.RaceCaption))
            return DdbRuleset.Dnd2014;
        return DdbRuleset.Unknown;
    }

    private static HashSet<string> Normalize(IReadOnlyCollection<string> captions)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (captions is null)
            return set;

        foreach (var caption in captions)
        {
            if (string.IsNullOrWhiteSpace(caption))
                continue;
            set.Add(caption.Trim().ToUpperInvariant());
        }

        return set;
    }
}
