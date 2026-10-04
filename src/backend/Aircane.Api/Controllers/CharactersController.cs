using System.Text;
using System.Text.Json;
using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Aircane.Application.Characters.Import;
using Aircane.Application.DTOs.Characters;
using Aircane.Application.GameSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Character CRUD - create, retrieve, update, delete, and list characters.
/// </summary>
[ApiController]
[Route("api/characters")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class CharactersController : ControllerBase
{
    /// <summary>Maximum size, in bytes, accepted on the source-adapter import path.</summary>
    private const int MaxAdapterPayloadBytes = 2 * 1024 * 1024;

    private readonly ICharacterService _characters;
    private readonly IPdfCharacterExtractor _pdfExtractor;
    private readonly ISystemRegistry _registry;
    private readonly ICharacterSchemaEngine _schemaEngine;
    private readonly CharacterFormatDetector _formatDetector;
    private readonly IReadOnlyList<ICharacterSourceMapper> _sourceMappers;
    private readonly ILogger<CharactersController> _logger;

    public CharactersController(
        ICharacterService characters,
        IPdfCharacterExtractor pdfExtractor,
        ISystemRegistry registry,
        ICharacterSchemaEngine schemaEngine,
        CharacterFormatDetector formatDetector,
        IEnumerable<ICharacterSourceMapper> sourceMappers,
        ILogger<CharactersController> logger)
    {
        _characters = characters;
        _pdfExtractor = pdfExtractor;
        _registry = registry;
        _schemaEngine = schemaEngine;
        _formatDetector = formatDetector;
        _sourceMappers = sourceMappers.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Imports a character from a canonical JSON string.
    /// Returns 201 with the created character on success.
    /// Returns 422 with a list of validation errors when the JSON is structurally valid but fails schema validation.
    /// Returns 400 when the JSON is malformed or the request body is missing required fields.
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(CharacterDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(SourceImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CharacterImportResult), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportCharacter(
        [FromBody] ImportCharacterJsonRequest request,
        [FromQuery] string? source,
        [FromQuery] Guid? gameSystemDefinitionId,
        CancellationToken cancellationToken)
    {
        // (1) canonicalJson is required on every path (the raw source text lives here for adapters).
        if (string.IsNullOrWhiteSpace(request.CanonicalJson))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "canonicalJson is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        // (2) Routing: the LEGACY branch is used only when no source was supplied AND the caller
        //     provided a gameSystem/ruleset (the historical canonical-JSON import contract). The
        //     ADAPTER branch is used when a source is given, OR when BOTH gameSystem and ruleset
        //     are absent (a raw Pathbuilder/Foundry/Roll20 upload).
        var legacyShaped = string.IsNullOrWhiteSpace(source) &&
                           (!string.IsNullOrWhiteSpace(request.GameSystem) ||
                            !string.IsNullOrWhiteSpace(request.Ruleset));

        if (legacyShaped)
            return await ImportCharacterLegacyAsync(request, cancellationToken);

        return await ImportCharacterViaAdapterAsync(
            request, source, gameSystemDefinitionId ?? request.GameSystemDefinitionId, cancellationToken);
    }

    /// <summary>
    /// The historical canonical-JSON import branch. Behaviour is byte-for-byte identical to the
    /// pre-adapter endpoint, including every legacy 400 guard.
    /// </summary>
    private async Task<IActionResult> ImportCharacterLegacyAsync(
        ImportCharacterJsonRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GameSystem))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "gameSystem is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(request.Ruleset))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "ruleset is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        CharacterImportResult result;
        try
        {
            result = await _characters.ImportCharacterFromJsonAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during character JSON import.");
            return BadRequest(new ProblemDetails
            {
                Title = "Import failed",
                Detail = "An unexpected error occurred while processing the import.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (!result.Success)
        {
            // Distinguish malformed JSON (400) from schema validation failures (422)
            var firstError = result.Errors.FirstOrDefault() ?? string.Empty;
            if (firstError.StartsWith("Invalid JSON:", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Malformed JSON",
                    Detail = firstError,
                    Status = StatusCodes.Status400BadRequest,
                });
            }

            return UnprocessableEntity(result);
        }

        return CreatedAtAction(nameof(GetCharacter), new { id = result.Character!.Id }, result.Character);
    }

    /// <summary>
    /// The source-adapter import branch: detect (or pin) the source format, map the raw JSON to a
    /// draft <see cref="CanonicalCharacter"/>, persist the draft, and return the review envelope.
    /// </summary>
    private async Task<IActionResult> ImportCharacterViaAdapterAsync(
        ImportCharacterJsonRequest request,
        string? source,
        Guid? gameSystemDefinitionId,
        CancellationToken cancellationToken)
    {
        // (3) Enforce the size cap before parsing (controller-constructed; body never echoed).
        if (Encoding.UTF8.GetByteCount(request.CanonicalJson) > MaxAdapterPayloadBytes)
            return BadRequest(new ProblemDetails
            {
                Title = "Payload too large",
                Detail = "Character JSON exceeds the 2 MB limit.",
                Status = StatusCodes.Status400BadRequest,
            });

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.CanonicalJson);
        }
        catch (JsonException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Malformed JSON",
                Detail = "The uploaded character JSON could not be parsed.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        using (doc)
        {
            // (4) Resolve the mapper: pin by source if supplied, else detect.
            CharacterImportSource detectedSource;
            ICharacterSourceMapper? mapper;
            if (!string.IsNullOrWhiteSpace(source))
            {
                if (!Enum.TryParse<CharacterImportSource>(source, ignoreCase: true, out var pinned) ||
                    pinned == CharacterImportSource.Unknown)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Validation failed",
                        Detail = "Unknown import source.",
                        Status = StatusCodes.Status400BadRequest,
                    });
                }

                mapper = _sourceMappers.FirstOrDefault(m => m.Source == pinned);
                if (mapper is null)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Validation failed",
                        Detail = "Unknown import source.",
                        Status = StatusCodes.Status400BadRequest,
                    });
                }

                detectedSource = pinned;
            }
            else
            {
                (detectedSource, mapper) = _formatDetector.Detect(doc);
            }

            // (5) Unknown / no mapper → prompt the user to pick a source. Persist nothing.
            if (mapper is null)
                return Ok(EmptySourceConfirmation(CharacterImportSource.Unknown));

            // (6) Resolve the game system.
            Guid resolvedDefinitionId;
            string gameSystemName;
            var forcedIdentifier = ForcedIdentifier(detectedSource);
            if (forcedIdentifier is not null)
            {
                var summary = await ResolveBuiltInSystemAsync(forcedIdentifier, cancellationToken);
                if (summary is null)
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Validation failed",
                        Detail = "The required built-in game system is not installed.",
                        Status = StatusCodes.Status400BadRequest,
                    });

                resolvedDefinitionId = summary.Id;
                gameSystemName = summary.Name;
            }
            else if (gameSystemDefinitionId.HasValue)
            {
                GameSystemDefinitionSummary? summary;
                try
                {
                    var definition = await _registry.GetByIdAsync(gameSystemDefinitionId.Value, cancellationToken);
                    summary = new GameSystemDefinitionSummary { Id = definition.Id, Name = definition.Name };
                }
                catch (KeyNotFoundException)
                {
                    summary = null;
                }

                if (summary is null)
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Validation failed",
                        Detail = "The specified game system is not installed.",
                        Status = StatusCodes.Status400BadRequest,
                    });

                resolvedDefinitionId = summary.Id;
                gameSystemName = summary.Name;
            }
            else
            {
                // Roll20 / Generic imply no system and none was supplied: ask the user to pick one.
                return Ok(new SourceImportResponse(
                    DetectedSource: detectedSource.ToString(),
                    Confidence: "low",
                    RequiresSourceConfirmation: true,
                    Ruleset: null,
                    RulesetRequiresConfirmation: false,
                    Review: new CharacterFieldReviewDto(
                        CharacterId: Guid.Empty,
                        ReviewRequired: true,
                        UnmappedFields: [],
                        Warnings: [])
                    {
                        RequiresGameSystemSelection = true,
                    }));
            }

            // (7) Map, persist, and build the review envelope.
            SourceMapResult mapResult;
            try
            {
                mapResult = mapper.Map(doc);
            }
            catch (Exception ex)
            {
                // Mappers are contractually non-throwing; guard anyway and surface as review-needed.
                _logger.LogError(ex, "Unexpected error while mapping imported character from {Source}.", detectedSource);
                return Ok(EmptySourceConfirmation(detectedSource));
            }

            var ruleset = mapResult.Ruleset ?? "2014";
            var persistWarnings = new List<string>();

            CharacterDto draft;
            try
            {
                draft = await PersistDraftFromCanonicalAsync(
                    mapResult.Character,
                    gameSystemName,
                    ruleset,
                    request.CampaignId,
                    request.OriginalFileName,
                    persistWarnings,
                    cancellationToken);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Adapter import could not persist a draft for {Source}.", detectedSource);
                return BadRequest(new ProblemDetails
                {
                    Title = "Import failed",
                    Detail = "The imported character data could not be saved.",
                    Status = StatusCodes.Status400BadRequest,
                });
            }

            var review = BuildSourceReview(mapResult, draft.Id, resolvedDefinitionId, persistWarnings);

            var confidence = mapResult.Confidence == ImportConfidence.High ? "high" : "low";
            return Ok(new SourceImportResponse(
                DetectedSource: detectedSource.ToString(),
                Confidence: confidence,
                RequiresSourceConfirmation: mapResult.Confidence == ImportConfidence.Low,
                Ruleset: mapResult.Ruleset,
                RulesetRequiresConfirmation: mapResult.RulesetRequiresConfirmation,
                Review: review));
        }
    }

    /// <summary>Builds the "please pick a source" response with an empty-but-valid review payload.</summary>
    private static SourceImportResponse EmptySourceConfirmation(CharacterImportSource detectedSource) =>
        new(
            DetectedSource: detectedSource.ToString(),
            Confidence: "low",
            RequiresSourceConfirmation: true,
            Ruleset: null,
            RulesetRequiresConfirmation: false,
            Review: new CharacterFieldReviewDto(
                CharacterId: Guid.Empty,
                ReviewRequired: true,
                UnmappedFields: [],
                Warnings: []));

    /// <summary>Returns the forced built-in system identifier for a source, or null when none applies.</summary>
    private static string? ForcedIdentifier(CharacterImportSource source) => source switch
    {
        CharacterImportSource.PathbuilderTwo or CharacterImportSource.FoundryPf2e => "pathfinder-2e-remaster",
        CharacterImportSource.DndBeyondApi or CharacterImportSource.DndBeyondCompanion or
            CharacterImportSource.FoundryDnd5e => "dnd-5e-2014",
        _ => null,
    };

    /// <summary>Resolves a built-in game system definition summary by its identifier via the registry.</summary>
    private async Task<GameSystemDefinitionSummary?> ResolveBuiltInSystemAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var systems = await _registry.ListAsync(cancellationToken);
        return systems.FirstOrDefault(s =>
            string.Equals(s.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Imports a character from a PDF file using form-field or text-layer extraction.
    /// Returns 200 with a <see cref="CharacterFieldReviewDto"/> when fields could not be
    /// confidently mapped (reviewRequired = true). The frontend should show the review UI
    /// before the character is considered final.
    /// Returns 200 with the extraction result when all fields mapped successfully.
    /// Returns 400 when the file is missing or the request is invalid.
    /// Returns 422 when the PDF requires OCR (no extractable text or form fields found).
    /// </summary>
    [HttpPost("import/pdf")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CharacterFieldReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PdfCharacterExtractionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportCharacterFromPdf(
        IFormFile file,
        [FromForm] string gameSystem,
        [FromForm] string ruleset,
        [FromForm] Guid? campaignId,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "A PDF file is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Only PDF files are accepted.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (string.IsNullOrWhiteSpace(gameSystem))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "gameSystem is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(ruleset))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "ruleset is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        PdfCharacterExtractionResult extraction;
        try
        {
            await using var stream = file.OpenReadStream();
            extraction = await _pdfExtractor.ExtractAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during PDF character extraction. File: {FileName}", file.FileName);
            return BadRequest(new ProblemDetails
            {
                Title = "Extraction failed",
                Detail = "An unexpected error occurred while reading the PDF.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (extraction.IsOcrRequired)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "OCR required",
                Detail = "This PDF contains no extractable text or form fields. " +
                         "OCR support is not available in the current version. " +
                         "Please use a form-fillable or text-readable PDF.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        // When there are unmapped fields, persist a draft character and return the review DTO
        if (extraction.UnmappedFields.Count > 0)
        {
            // Persist the draft character so the review UI has an ID to work with
            var draftCharacter = await PersistDraftFromExtractionAsync(
                extraction, gameSystem, ruleset, campaignId, file.FileName, cancellationToken);

            // Build the unmapped field DTOs with confidence scores
            var unmappedDtos = extraction.UnmappedFields
                .Select(kvp => BuildUnmappedFieldDto(kvp.Key, kvp.Value))
                .ToList();

            var reviewDto = new CharacterFieldReviewDto(
                CharacterId: draftCharacter.Id,
                ReviewRequired: true,
                UnmappedFields: unmappedDtos,
                Warnings: extraction.Warnings);

            return Ok(reviewDto);
        }

        // All fields mapped - return the extraction result for the frontend review UI
        return Ok(extraction);
    }

    /// <summary>
    /// Applies user-confirmed field mappings to a character's CanonicalJson and marks it as reviewed.
    /// Returns 200 with the updated character on success.
    /// Returns 400 when the request is invalid or a field path/value is unrecognised.
    /// Returns 404 when the character does not exist.
    /// </summary>
    [HttpPut("{id:guid}/field-mappings")]
    [ProducesResponseType(typeof(CharacterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyFieldMappings(
        Guid id,
        [FromBody] ApplyFieldMappingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Mappings is null)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "A mappings array is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        try
        {
            var dto = await _characters.ApplyFieldMappingsAsync(id, request, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Mapping failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Persists a draft character from a PDF extraction result so the review UI has an ID.
    /// </summary>
    private async Task<CharacterDto> PersistDraftFromExtractionAsync(
        PdfCharacterExtractionResult extraction,
        string gameSystem,
        string ruleset,
        Guid? campaignId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var mapped = extraction.MappedCharacter ?? new Application.Characters.CanonicalCharacter();
        var name = string.IsNullOrWhiteSpace(mapped.Identity.Name)
            ? System.IO.Path.GetFileNameWithoutExtension(fileName)
            : mapped.Identity.Name;

        var createRequest = new CreateCharacterRequest(
            Name: name,
            GameSystem: gameSystem,
            Ruleset: ruleset,
            Level: Math.Max(1, mapped.Classes.Sum(c => c.Level)),
            CanonicalJson: Application.Characters.CharacterJsonSerializer.Serialize(mapped),
            CampaignId: campaignId,
            OwnerParticipantId: null);

        try
        {
            return await _characters.CreateCharacterAsync(createRequest, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "Draft character validation warning during PDF import ({FileName}): {Message}",
                fileName, ex.Message);

            // Fall back to a minimal valid character if validation fails
            var minimal = new Application.Characters.CanonicalCharacter();
            minimal.Identity.Name = name;
            minimal.Classes.Add(new Application.Characters.CharacterClass
            {
                ClassName = "Unknown",
                Level = 1,
                HitDie = 8
            });

            var fallbackRequest = new CreateCharacterRequest(
                Name: name,
                GameSystem: gameSystem,
                Ruleset: ruleset,
                Level: 1,
                CanonicalJson: Application.Characters.CharacterJsonSerializer.Serialize(minimal),
                CampaignId: campaignId,
                OwnerParticipantId: null);

            return await _characters.CreateCharacterAsync(fallbackRequest, cancellationToken);
        }
    }

    /// <summary>
    /// Persists a draft character from a mapped <see cref="CanonicalCharacter"/> (the source-adapter
    /// path). Unlike <see cref="PersistDraftFromExtractionAsync"/>, this NEVER constructs an
    /// "Unknown"/level-1 stub that discards mapped data: if validation fails, it retries once with
    /// only a defaulted name and clamped level, keeping every value the mapper produced, and records
    /// a warning onto <paramref name="warnings"/> so the review UI surfaces it.
    /// </summary>
    private async Task<CharacterDto> PersistDraftFromCanonicalAsync(
        CanonicalCharacter mapped,
        string gameSystem,
        string ruleset,
        Guid? campaignId,
        string? originalFileName,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var name = DeriveDraftName(mapped, originalFileName);
        var level = Math.Max(1, mapped.Classes.Sum(c => c.Level));

        var createRequest = new CreateCharacterRequest(
            Name: name,
            GameSystem: gameSystem,
            Ruleset: ruleset,
            Level: level,
            CanonicalJson: Application.Characters.CharacterJsonSerializer.Serialize(mapped),
            CampaignId: campaignId,
            OwnerParticipantId: null);

        try
        {
            return await _characters.CreateCharacterAsync(createRequest, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // Preserve the mapped data as-is: default only the name (never substitute an Unknown
            // stub). We mutate the SAME CanonicalCharacter so all mapped values survive the retry.
            _logger.LogWarning(
                "Adapter draft validation warning ({FileName}): {Message}. Persisting partial data as-is.",
                originalFileName ?? "n/a", ex.Message);
            warnings.Add("Some imported values could not be validated automatically. Please review them before saving.");

            if (string.IsNullOrWhiteSpace(mapped.Identity.Name))
                mapped.Identity.Name = name;

            var retryRequest = new CreateCharacterRequest(
                Name: name,
                GameSystem: gameSystem,
                Ruleset: ruleset,
                Level: level,
                CanonicalJson: Application.Characters.CharacterJsonSerializer.Serialize(mapped),
                CampaignId: campaignId,
                OwnerParticipantId: null);

            return await _characters.CreateCharacterAsync(retryRequest, cancellationToken);
        }
    }

    /// <summary>Derives the draft character name from the mapped data, falling back to the file name.</summary>
    private static string DeriveDraftName(CanonicalCharacter mapped, string? originalFileName)
    {
        if (!string.IsNullOrWhiteSpace(mapped.Identity.Name))
            return mapped.Identity.Name;

        if (!string.IsNullOrWhiteSpace(originalFileName))
            return System.IO.Path.GetFileNameWithoutExtension(originalFileName);

        return "Imported Character";
    }

    /// <summary>
    /// Builds the review payload for a source-adapter import. Echoes the mapper's
    /// <see cref="SourceMapResult.MappedFields"/> verbatim (NO flatten), turns each
    /// <see cref="SourceMapResult.ExtraFields"/> entry into an <see cref="UnmappedFieldDto"/> marked
    /// for review, and flags per-field review from <see cref="SourceMapResult.RequiresReviewPaths"/>.
    /// </summary>
    private static CharacterFieldReviewDto BuildSourceReview(
        SourceMapResult result,
        Guid characterId,
        Guid gameSystemDefinitionId,
        IReadOnlyList<string> extraWarnings)
    {
        var unmapped = result.ExtraFields
            .Select(kvp => new UnmappedFieldDto(
                SourceFieldName: kvp.Key,
                SourceValue: kvp.Value,
                SuggestedCanonicalField: null,
                Confidence: 0f)
            {
                RequiresReview = true,
            })
            .ToList();

        // Mapped paths the mapper flagged for review (e.g. a derived HP value) are surfaced as
        // review-required entries whose suggested canonical field is the mapped path itself, so the
        // review UI can prompt confirmation while the value still lives in MappedFields.
        foreach (var path in result.RequiresReviewPaths)
        {
            if (result.MappedFields.TryGetValue(path, out var value))
            {
                unmapped.Add(new UnmappedFieldDto(
                    SourceFieldName: path,
                    SourceValue: value,
                    SuggestedCanonicalField: path,
                    Confidence: 1f)
                {
                    RequiresReview = true,
                });
            }
        }

        var warnings = extraWarnings.Concat(result.Warnings).ToList();

        return new CharacterFieldReviewDto(
            CharacterId: characterId,
            ReviewRequired: true,
            UnmappedFields: unmapped,
            Warnings: warnings)
        {
            GameSystemDefinitionId = gameSystemDefinitionId,
            MappedFields = result.MappedFields,
            RequiresGameSystemSelection = false,
        };
    }

    /// <summary>
    /// Builds an <see cref="UnmappedFieldDto"/> for a field that could not be automatically mapped.
    /// Applies simple heuristics to suggest a canonical field and assign a confidence score.
    /// </summary>
    private static UnmappedFieldDto BuildUnmappedFieldDto(string sourceFieldName, string sourceValue)
    {
        var (suggested, confidence) = SuggestCanonicalField(sourceFieldName);
        return new UnmappedFieldDto(
            SourceFieldName: sourceFieldName,
            SourceValue: sourceValue,
            SuggestedCanonicalField: suggested,
            Confidence: confidence);
    }

    /// <summary>
    /// Heuristic mapping from unknown source field names to canonical field paths.
    /// Returns (null, 0f) when no suggestion can be made.
    /// </summary>
    private static (string? Suggested, float Confidence) SuggestCanonicalField(string fieldName)
    {
        var normalized = fieldName.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

        return normalized switch
        {
            var n when n.Contains("name") => ("identity.name", 0.7f),
            var n when n.Contains("race") || n.Contains("ancestry") => ("race", 0.7f),
            var n when n.Contains("class") => ("class", 0.6f),
            var n when n.Contains("level") => ("level", 0.6f),
            var n when n.Contains("background") => ("identity.background", 0.7f),
            var n when n.Contains("alignment") => ("identity.alignment", 0.7f),
            var n when n.Contains("xp") || n.Contains("exp") || n.Contains("experience") => ("experiencePoints", 0.6f),
            var n when n.Contains("str") || n.Contains("strength") => ("abilities.strength", 0.65f),
            var n when n.Contains("dex") || n.Contains("dexterity") => ("abilities.dexterity", 0.65f),
            var n when n.Contains("con") || n.Contains("constitution") => ("abilities.constitution", 0.65f),
            var n when n.Contains("int") || n.Contains("intelligence") => ("abilities.intelligence", 0.65f),
            var n when n.Contains("wis") || n.Contains("wisdom") => ("abilities.wisdom", 0.65f),
            var n when n.Contains("cha") || n.Contains("charisma") => ("abilities.charisma", 0.65f),
            var n when n.Contains("ac") || n.Contains("armor") || n.Contains("armour") => ("combat.armorClass", 0.65f),
            var n when n.Contains("hp") || n.Contains("hitpoint") || n.Contains("health") => ("combat.maxHitPoints", 0.6f),
            var n when n.Contains("speed") || n.Contains("movement") => ("combat.speed", 0.65f),
            var n when n.Contains("initiative") => ("combat.initiative", 0.65f),
            var n when n.Contains("proficiency") || n.Contains("profbonus") => ("combat.proficiencyBonus", 0.65f),
            var n when n.Contains("passive") || n.Contains("perception") => ("passivePerception", 0.55f),
            _ => (null, 0f)
        };
    }

    /// <summary>
    /// Creates a new character manually.
    /// Validates character data against the campaign's bound Character Schema when available.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CharacterDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCharacter(
        [FromBody] CreateCharacterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Character name is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(request.GameSystem))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Game system is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(request.Ruleset))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Ruleset is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (request.Level < 1 || request.Level > 20)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Level must be between 1 and 20.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Schema-aware validation: if a campaign is specified, validate against its bound schema
        if (request.CampaignId.HasValue && !string.IsNullOrWhiteSpace(request.CanonicalJson))
        {
            var schemaValidation = await ValidateAgainstCampaignSchemaAsync(
                request.CampaignId.Value, request.CanonicalJson, cancellationToken);
            if (schemaValidation is not null)
                return schemaValidation;
        }

        try
        {
            var dto = await _characters.CreateCharacterAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetCharacter), new { id = dto.Id }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Returns a single character by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(CharacterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCharacter(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _characters.GetCharacterAsync(id, cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Updates an existing character's editable fields.
    /// Validates character data against the campaign's bound Character Schema when available.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CharacterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCharacter(
        Guid id,
        [FromBody] UpdateCharacterRequest request,
        CancellationToken cancellationToken)
    {
        // If canonical JSON is being updated and a campaign is specified, validate against schema
        if (request.CampaignId.HasValue && !string.IsNullOrWhiteSpace(request.CanonicalJson))
        {
            var schemaValidation = await ValidateAgainstCampaignSchemaAsync(
                request.CampaignId.Value, request.CanonicalJson, cancellationToken);
            if (schemaValidation is not null)
                return schemaValidation;
        }

        try
        {
            var dto = await _characters.UpdateCharacterAsync(id, request, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Deletes a character by ID.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCharacter(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _characters.DeleteCharacterAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Lists characters scoped to a campaign.
    /// Pass <c>participantId</c> to scope results to a specific participant (player view).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<CharacterDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListCharacters(
        [FromQuery] Guid? campaignId,
        [FromQuery] Guid? participantId,
        CancellationToken cancellationToken)
    {
        if (!campaignId.HasValue)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "campaignId query parameter is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        var characters = await _characters.ListByCampaignAsync(
            campaignId.Value,
            participantId,
            cancellationToken);

        return Ok(characters);
    }

    // ── Template endpoints ────────────────────────────────────────────────────

    /// <summary>
    /// Saves a character import field mapping as a reusable template.
    /// Returns 201 with the created template on success.
    /// Returns 400 when required fields are missing.
    /// </summary>
    [HttpPost("templates")]
    [ProducesResponseType(typeof(CharacterTemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveTemplate(
        [FromBody] SaveTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Template name is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(request.GameSystem))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "gameSystem is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(request.Ruleset))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "ruleset is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        try
        {
            var dto = await _characters.SaveTemplateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTemplate), new { id = dto.Id }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Lists all saved character templates.
    /// Pass <c>?gameSystem=</c> to filter by game system.
    /// </summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(IReadOnlyList<CharacterTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTemplates(
        [FromQuery] string? gameSystem,
        CancellationToken cancellationToken)
    {
        var templates = await _characters.ListTemplatesAsync(gameSystem, cancellationToken);
        return Ok(templates);
    }

    /// <summary>
    /// Returns a single character template by ID.
    /// </summary>
    [HttpGet("templates/{id:guid}")]
    [ProducesResponseType(typeof(CharacterTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _characters.GetTemplateAsync(id, cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    // ── Schema Validation Helper ─────────────────────────────────────────────

    /// <summary>
    /// Validates character JSON against the campaign's bound Character Schema.
    /// Returns null if validation passes or no schema is bound; returns a BadRequest result if validation fails.
    /// Also flags mismatches when the character's definition differs from the campaign's definition.
    /// </summary>
    private async Task<IActionResult?> ValidateAgainstCampaignSchemaAsync(
        Guid campaignId,
        string characterJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = await _registry.GetByCampaignAsync(campaignId, cancellationToken);
            if (definition.CharacterSchema is null)
                return null; // No schema bound - freeform mode

            var validationResult = _schemaEngine.Validate(characterJson, definition.CharacterSchema);
            if (!validationResult.IsValid)
            {
                var errorDetails = string.Join("; ",
                    validationResult.Errors.Select(e => $"{e.FieldPath}: {e.Message}"));

                return BadRequest(new ProblemDetails
                {
                    Title = "Character schema validation failed",
                    Detail = errorDetails,
                    Status = StatusCodes.Status400BadRequest,
                    Extensions =
                    {
                        ["errors"] = validationResult.Errors,
                        ["gameSystemDefinitionId"] = definition.Id,
                    }
                });
            }

            return null; // Validation passed
        }
        catch (KeyNotFoundException)
        {
            // No definition bound to this campaign - skip schema validation
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schema validation skipped for campaign {CampaignId}: {Message}",
                campaignId, ex.Message);
            return null; // Graceful degradation - don't block character creation
        }
    }
}
