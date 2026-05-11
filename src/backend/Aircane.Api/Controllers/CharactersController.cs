using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Aircane.Application.DTOs.Characters;
using Aircane.Application.GameSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Character CRUD — create, retrieve, update, delete, and list characters.
/// </summary>
[ApiController]
[Route("api/characters")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class CharactersController : ControllerBase
{
    private readonly ICharacterService _characters;
    private readonly IPdfCharacterExtractor _pdfExtractor;
    private readonly ISystemRegistry _registry;
    private readonly ICharacterSchemaEngine _schemaEngine;
    private readonly ILogger<CharactersController> _logger;

    public CharactersController(
        ICharacterService characters,
        IPdfCharacterExtractor pdfExtractor,
        ISystemRegistry registry,
        ICharacterSchemaEngine schemaEngine,
        ILogger<CharactersController> logger)
    {
        _characters = characters;
        _pdfExtractor = pdfExtractor;
        _registry = registry;
        _schemaEngine = schemaEngine;
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CharacterImportResult), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportCharacter(
        [FromBody] ImportCharacterJsonRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CanonicalJson))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "canonicalJson is required.",
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

        // All fields mapped — return the extraction result for the frontend review UI
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
                return null; // No schema bound — freeform mode

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
            // No definition bound to this campaign — skip schema validation
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schema validation skipped for campaign {CampaignId}: {Message}",
                campaignId, ex.Message);
            return null; // Graceful degradation — don't block character creation
        }
    }
}
