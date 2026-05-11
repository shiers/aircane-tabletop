using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Aircane.Application.DTOs.Characters;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Characters;

/// <summary>
/// EF Core-backed implementation of <see cref="ICharacterService"/>.
/// Handles manual character creation, retrieval, update, deletion, and campaign listing.
/// </summary>
public sealed class CharacterService : ICharacterService
{
    private readonly AircaneDbContext _db;
    private readonly CharacterSchemaValidator _validator;
    private readonly IPdfCharacterExtractor _pdfExtractor;
    private readonly ILogger<CharacterService> _logger;

    public CharacterService(
        AircaneDbContext db,
        CharacterSchemaValidator validator,
        IPdfCharacterExtractor pdfExtractor,
        ILogger<CharacterService> logger)
    {
        _db = db;
        _validator = validator;
        _pdfExtractor = pdfExtractor;
        _logger = logger;
    }

    // ── CreateCharacterAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterDto> CreateCharacterAsync(
        CreateCharacterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Deserialize and validate the canonical character data
        CanonicalCharacter canonical;
        try
        {
            canonical = CharacterJsonSerializer.Deserialize(request.CanonicalJson)
                ?? throw new ArgumentException("CanonicalJson must be a valid character JSON object.", nameof(request));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"CanonicalJson is not valid JSON: {ex.Message}", nameof(request), ex);
        }

        var validationResult = await _validator.ValidateAsync(canonical, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new ArgumentException($"Character validation failed: {errors}", nameof(request));
        }

        // Serialize back to ensure consistent formatting
        var canonicalJson = CharacterJsonSerializer.Serialize(canonical);

        // Build initial runtime state from canonical data
        var currentState = new CharacterCurrentState
        {
            CurrentHitPoints = canonical.Combat.CurrentHitPoints,
            TemporaryHitPoints = canonical.Combat.TemporaryHitPoints,
            Conditions = [],
        };
        var currentStateJson = CharacterJsonSerializer.SerializeState(currentState);

        var character = new Character(
            name: request.Name,
            gameSystem: request.GameSystem,
            ruleset: request.Ruleset,
            level: request.Level,
            canonicalJson: canonicalJson,
            currentStateJson: currentStateJson,
            campaignId: request.CampaignId,
            ownerParticipantId: request.OwnerParticipantId);

        _db.Characters.Add(character);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Character created: {CharacterId} '{Name}' (Campaign: {CampaignId})",
            character.Id, character.Name, character.CampaignId);

        return ToDto(character);
    }

    // ── GetCharacterAsync ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterDto?> GetCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var character = await _db.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == characterId, cancellationToken);

        return character is null ? null : ToDto(character);
    }

    // ── UpdateCharacterAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterDto> UpdateCharacterAsync(
        Guid characterId,
        UpdateCharacterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var character = await _db.Characters
            .FirstOrDefaultAsync(c => c.Id == characterId, cancellationToken)
            ?? throw new KeyNotFoundException($"Character '{characterId}' not found.");

        if (request.Name is not null)
            character.Name = request.Name;

        if (request.Level.HasValue)
            character.Level = request.Level.Value;

        if (request.OwnerParticipantId.HasValue)
            character.OwnerParticipantId = request.OwnerParticipantId.Value;

        if (request.CanonicalJson is not null)
        {
            CanonicalCharacter canonical;
            try
            {
                canonical = CharacterJsonSerializer.Deserialize(request.CanonicalJson)
                    ?? throw new ArgumentException("CanonicalJson must be a valid character JSON object.", nameof(request));
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArgumentException($"CanonicalJson is not valid JSON: {ex.Message}", nameof(request), ex);
            }

            var validationResult = await _validator.ValidateAsync(canonical, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                throw new ArgumentException($"Character validation failed: {errors}", nameof(request));
            }

            character.CanonicalJson = CharacterJsonSerializer.Serialize(canonical);
        }

        if (request.CurrentStateJson is not null)
            character.CurrentStateJson = request.CurrentStateJson;

        character.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Character updated: {CharacterId}", characterId);

        return ToDto(character);
    }

    // ── DeleteCharacterAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task DeleteCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var character = await _db.Characters
            .FirstOrDefaultAsync(c => c.Id == characterId, cancellationToken)
            ?? throw new KeyNotFoundException($"Character '{characterId}' not found.");

        // Unassign the character from any session participants that have it assigned
        var assignedParticipants = await _db.SessionParticipants
            .Where(p => p.CharacterId == characterId)
            .ToListAsync(cancellationToken);

        foreach (var participant in assignedParticipants)
            participant.CharacterId = null;

        _db.Characters.Remove(character);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Character deleted: {CharacterId} (unassigned from {ParticipantCount} participant(s))",
            characterId, assignedParticipants.Count);
    }

    // ── ListByCampaignAsync ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<CharacterDto>> ListByCampaignAsync(
        Guid campaignId,
        Guid? requestingParticipantId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Characters
            .AsNoTracking()
            .Where(c => c.CampaignId == campaignId);

        // If a participant ID is provided, scope to their own characters only
        if (requestingParticipantId.HasValue)
            query = query.Where(c => c.OwnerParticipantId == requestingParticipantId.Value);

        var characters = await query
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return characters.Select(ToDto).ToList();
    }

    // ── ImportCharacterAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterDto> ImportCharacterAsync(
        ImportCharacterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Extract fields from the PDF
        var extraction = await _pdfExtractor.ExtractAsync(request.FileContent, cancellationToken);

        // 2. OCR required — cannot proceed without text content
        if (extraction.IsOcrRequired)
        {
            throw new InvalidOperationException(
                "This PDF contains no extractable text or form fields. " +
                "OCR support is not available in the current version. " +
                "Please use a form-fillable or text-readable PDF.");
        }

        // 3. No name mapped — return a draft character for manual review
        var mapped = extraction.MappedCharacter;
        if (mapped is null || string.IsNullOrWhiteSpace(mapped.Identity.Name))
        {
            _logger.LogInformation(
                "PDF import produced no character name; returning draft for review. File: {FileName}",
                request.OriginalFileName);

            // Return a draft with empty name so the frontend can show the review UI
            var draft = mapped ?? new CanonicalCharacter();
            var draftJson = CharacterJsonSerializer.Serialize(draft);
            var draftState = new CharacterCurrentState
            {
                CurrentHitPoints = draft.Combat.CurrentHitPoints,
                TemporaryHitPoints = draft.Combat.TemporaryHitPoints,
                Conditions = [],
            };

            var draftCharacter = new Character(
                name: string.Empty,
                gameSystem: request.GameSystem,
                ruleset: request.Ruleset,
                level: draft.Classes.Sum(c => c.Level),
                canonicalJson: draftJson,
                currentStateJson: CharacterJsonSerializer.SerializeState(draftState),
                campaignId: request.CampaignId,
                ownerParticipantId: request.OwnerParticipantId);

            _db.Characters.Add(draftCharacter);
            await _db.SaveChangesAsync(cancellationToken);

            return ToDto(draftCharacter);
        }

        // 4. Validate the mapped character
        var validationResult = await _validator.ValidateAsync(mapped, cancellationToken);
        if (!validationResult.IsValid)
        {
            // Log validation issues but still persist as a draft for review
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            _logger.LogWarning(
                "PDF import validation warnings for '{Name}': {Errors}",
                mapped.Identity.Name, errors);
        }

        // 5. Persist the mapped character
        var canonicalJson = CharacterJsonSerializer.Serialize(mapped);
        var currentState = new CharacterCurrentState
        {
            CurrentHitPoints = mapped.Combat.CurrentHitPoints,
            TemporaryHitPoints = mapped.Combat.TemporaryHitPoints,
            Conditions = [],
        };

        var character = new Character(
            name: mapped.Identity.Name,
            gameSystem: request.GameSystem,
            ruleset: request.Ruleset,
            level: Math.Max(1, mapped.Classes.Sum(c => c.Level)),
            canonicalJson: canonicalJson,
            currentStateJson: CharacterJsonSerializer.SerializeState(currentState),
            campaignId: request.CampaignId,
            ownerParticipantId: request.OwnerParticipantId);

        _db.Characters.Add(character);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Character imported from PDF: {CharacterId} '{Name}' (Campaign: {CampaignId}, File: {FileName})",
            character.Id, character.Name, character.CampaignId, request.OriginalFileName);

        return ToDto(character);
    }

    // ── ImportCharacterFromJsonAsync ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterImportResult> ImportCharacterFromJsonAsync(
        ImportCharacterJsonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CanonicalJson))
            return CharacterImportResult.Fail("JSON content is required.");

        // 1. Parse JSON — catch malformed input
        CanonicalCharacter canonical;
        try
        {
            canonical = CharacterJsonSerializer.Deserialize(request.CanonicalJson)
                ?? throw new System.Text.Json.JsonException("Deserialized to null.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning("JSON character import failed — malformed JSON: {Message}", ex.Message);
            return CharacterImportResult.Fail($"Invalid JSON: {ex.Message}");
        }

        // 2. Validate against canonical schema
        var validationResult = await _validator.ValidateAsync(canonical, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => e.ErrorMessage)
                .ToList();

            _logger.LogWarning(
                "JSON character import failed — {ErrorCount} validation error(s): {Errors}",
                errors.Count,
                string.Join("; ", errors));

            return CharacterImportResult.Fail(errors);
        }

        // 3. Persist the character
        var canonicalJson = CharacterJsonSerializer.Serialize(canonical);

        var currentState = new CharacterCurrentState
        {
            CurrentHitPoints = canonical.Combat.CurrentHitPoints,
            TemporaryHitPoints = canonical.Combat.TemporaryHitPoints,
            Conditions = [],
        };
        var currentStateJson = CharacterJsonSerializer.SerializeState(currentState);

        // Derive top-level fields from canonical data
        var name = canonical.Identity.Name;
        var level = canonical.Classes.Sum(c => c.Level);

        var character = new Character(
            name: name,
            gameSystem: request.GameSystem,
            ruleset: request.Ruleset,
            level: level,
            canonicalJson: canonicalJson,
            currentStateJson: currentStateJson,
            campaignId: request.CampaignId,
            ownerParticipantId: request.OwnerParticipantId);

        _db.Characters.Add(character);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Character imported from JSON: {CharacterId} '{Name}' (Campaign: {CampaignId}, File: {FileName})",
            character.Id, character.Name, character.CampaignId, request.OriginalFileName ?? "n/a");

        return CharacterImportResult.Ok(ToDto(character));
    }

    // ── ApplyFieldMappingsAsync ───────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterDto> ApplyFieldMappingsAsync(
        Guid characterId,
        ApplyFieldMappingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var character = await _db.Characters
            .FirstOrDefaultAsync(c => c.Id == characterId, cancellationToken)
            ?? throw new KeyNotFoundException($"Character '{characterId}' not found.");

        // Deserialize the current canonical JSON
        var canonical = CharacterJsonSerializer.DeserializeOrDefault(character.CanonicalJson);

        // Apply each mapping
        var errors = new List<string>();
        foreach (var mapping in request.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.CanonicalFieldPath))
                continue;

            try
            {
                ApplyMapping(canonical, mapping.CanonicalFieldPath.Trim(), mapping.Value ?? string.Empty);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Field '{mapping.CanonicalFieldPath}': {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors));

        // Sync top-level fields from canonical data
        if (!string.IsNullOrWhiteSpace(canonical.Identity.Name))
            character.Name = canonical.Identity.Name;

        var totalLevel = canonical.Classes.Sum(c => c.Level);
        if (totalLevel > 0)
            character.Level = totalLevel;

        character.CanonicalJson = CharacterJsonSerializer.Serialize(canonical);
        character.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Field mappings applied to character {CharacterId}: {MappingCount} mapping(s).",
            characterId, request.Mappings.Count);

        return ToDto(character);
    }

    /// <summary>
    /// Applies a single field mapping to the canonical character using a dot-separated path.
    /// Supported paths mirror the canonical field list exposed in the review UI.
    /// </summary>
    private static void ApplyMapping(CanonicalCharacter character, string path, string value)
    {
        switch (path.ToLowerInvariant())
        {
            // ── Identity ──────────────────────────────────────────────────────
            case "identity.name":
            case "name":
                character.Identity.Name = value;
                break;
            case "identity.raceorancestry":
            case "race":
                character.Identity.RaceOrAncestry = value;
                break;
            case "identity.background":
            case "background":
                character.Identity.Background = value;
                break;
            case "identity.alignment":
            case "alignment":
                character.Identity.Alignment = value;
                break;
            case "identity.experiencepoints":
            case "experiencepoints":
                // experiencePoints is not a first-class field on CanonicalCharacter;
                // store in Notes as a fallback until the schema is extended.
                character.Notes = AppendNote(character.Notes, $"Experience Points: {value}");
                break;

            // ── Class / Level ─────────────────────────────────────────────────
            case "class":
                if (character.Classes.Count > 0)
                    character.Classes[0].ClassName = value;
                else
                    character.Classes.Add(new CharacterClass { ClassName = value, Level = 1, HitDie = 8 });
                break;
            case "level":
                if (int.TryParse(value, out var level) && level >= 1)
                {
                    if (character.Classes.Count > 0)
                        character.Classes[0].Level = level;
                    else
                        character.Classes.Add(new CharacterClass { ClassName = "Unknown", Level = level, HitDie = 8 });
                }
                else
                    throw new ArgumentException($"'{value}' is not a valid level (must be a positive integer).");
                break;

            // ── Abilities ─────────────────────────────────────────────────────
            case "abilities.strength":
            case "strength":
                character.Abilities.Strength = ParseIntMapping(path, value, 1, 30);
                break;
            case "abilities.dexterity":
            case "dexterity":
                character.Abilities.Dexterity = ParseIntMapping(path, value, 1, 30);
                break;
            case "abilities.constitution":
            case "constitution":
                character.Abilities.Constitution = ParseIntMapping(path, value, 1, 30);
                break;
            case "abilities.intelligence":
            case "intelligence":
                character.Abilities.Intelligence = ParseIntMapping(path, value, 1, 30);
                break;
            case "abilities.wisdom":
            case "wisdom":
                character.Abilities.Wisdom = ParseIntMapping(path, value, 1, 30);
                break;
            case "abilities.charisma":
            case "charisma":
                character.Abilities.Charisma = ParseIntMapping(path, value, 1, 30);
                break;

            // ── Combat ────────────────────────────────────────────────────────
            case "combat.armorclass":
            case "armorclass":
                character.Combat.ArmorClass = ParseIntMapping(path, value, 0, 99);
                break;
            case "combat.hitpoints":
            case "combat.currenthitpoints":
            case "hitpoints":
                character.Combat.CurrentHitPoints = ParseIntMapping(path, value, 0, 9999);
                break;
            case "combat.maxhitpoints":
            case "maxhitpoints":
                character.Combat.MaxHitPoints = ParseIntMapping(path, value, 0, 9999);
                break;
            case "combat.speed":
            case "speed":
                character.Combat.Speed = ParseIntMapping(path, value, 0, 999);
                break;
            case "combat.initiative":
            case "initiative":
                character.Combat.Initiative = ParseIntMapping(path, value, -20, 20);
                break;
            case "combat.proficiencybonus":
            case "proficiencybonus":
                character.Combat.ProficiencyBonus = ParseIntMapping(path, value, 0, 10);
                break;

            // ── Derived / computed ────────────────────────────────────────────
            case "passiveperception":
                // Passive perception is derived; store in notes as a reference value.
                character.Notes = AppendNote(character.Notes, $"Passive Perception: {value}");
                break;

            default:
                throw new ArgumentException($"Unknown canonical field path '{path}'.");
        }
    }

    private static int ParseIntMapping(string fieldPath, string value, int min, int max)
    {
        // Strip common suffixes like "ft." or modifier notation "(+3)"
        var cleaned = value.Split([' ', '('])[0].Trim().TrimEnd('f', 't', '.');
        if (!int.TryParse(cleaned, out var result))
            throw new ArgumentException($"'{value}' is not a valid integer for field '{fieldPath}'.");
        if (result < min || result > max)
            throw new ArgumentException($"Value {result} is out of range [{min}, {max}] for field '{fieldPath}'.");
        return result;
    }

    private static string AppendNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return note;
        return $"{existing}\n{note}";
    }

    // ── SaveTemplateAsync ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterTemplateDto> SaveTemplateAsync(
        SaveTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Template name is required.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.GameSystem))
            throw new ArgumentException("Game system is required.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Ruleset))
            throw new ArgumentException("Ruleset is required.", nameof(request));

        var fieldMappingsJson = System.Text.Json.JsonSerializer.Serialize(
            request.FieldDefinitions,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        var template = new CharacterTemplate(
            name: request.Name,
            gameSystem: request.GameSystem,
            ruleset: request.Ruleset,
            fieldMappingsJson: fieldMappingsJson);

        _db.CharacterTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Character template saved: {TemplateId} '{Name}' ({GameSystem}/{Ruleset})",
            template.Id, template.Name, template.GameSystem, template.Ruleset);

        return ToTemplateDto(template);
    }

    // ── ListTemplatesAsync ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<CharacterTemplateDto>> ListTemplatesAsync(
        string? gameSystem = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CharacterTemplates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(gameSystem))
            query = query.Where(t => t.GameSystem == gameSystem);

        var templates = await query
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return templates.Select(ToTemplateDto).ToList();
    }

    // ── GetTemplateAsync ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CharacterTemplateDto?> GetTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.CharacterTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        return template is null ? null : ToTemplateDto(template);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static CharacterDto ToDto(Character c) => new(
        Id: c.Id,
        CampaignId: c.CampaignId,
        OwnerParticipantId: c.OwnerParticipantId,
        Name: c.Name,
        GameSystem: c.GameSystem,
        Ruleset: c.Ruleset,
        Level: c.Level,
        CanonicalJson: c.CanonicalJson,
        CurrentStateJson: c.CurrentStateJson,
        CreatedAt: c.CreatedAt,
        UpdatedAt: c.UpdatedAt);

    private static CharacterTemplateDto ToTemplateDto(CharacterTemplate t)
    {
        IReadOnlyList<TemplateFieldDefinition> fields;
        try
        {
            fields = System.Text.Json.JsonSerializer.Deserialize<List<TemplateFieldDefinition>>(
                t.FieldMappingsJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? [];
        }
        catch
        {
            fields = [];
        }

        return new CharacterTemplateDto(
            Id: t.Id,
            Name: t.Name,
            GameSystem: t.GameSystem,
            Ruleset: t.Ruleset,
            FieldDefinitions: fields,
            CreatedAt: t.CreatedAt,
            UpdatedAt: t.UpdatedAt);
    }
}
