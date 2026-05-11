using Aircane.Api.Authorization;
using Aircane.Application.DTOs.GameSystems;
using Aircane.Application.GameSystems;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Game System Definition management — CRUD, import/export, validation, templates, and previews.
/// </summary>
[ApiController]
[Route("api/game-systems")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class GameSystemsController : ControllerBase
{
    private readonly ISystemRegistry _registry;
    private readonly IMechanicResolver _mechanicResolver;
    private readonly ICharacterSchemaEngine _schemaEngine;
    private readonly ILogger<GameSystemsController> _logger;

    public GameSystemsController(
        ISystemRegistry registry,
        IMechanicResolver mechanicResolver,
        ICharacterSchemaEngine schemaEngine,
        ILogger<GameSystemsController> logger)
    {
        _registry = registry;
        _mechanicResolver = mechanicResolver;
        _schemaEngine = schemaEngine;
        _logger = logger;
    }

    // ── CRUD Endpoints ────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all active Game System Definitions (summary DTOs).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<GameSystemDefinitionSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGameSystems(CancellationToken cancellationToken)
    {
        var summaries = await _registry.ListAsync(cancellationToken);
        var dtos = summaries.Select(s => new GameSystemDefinitionSummaryDto(
            Id: s.Id,
            Identifier: s.Identifier,
            Name: s.Name,
            Version: s.Version,
            Genre: s.Genre,
            License: s.License,
            IsBuiltIn: s.IsBuiltIn,
            IsActive: s.IsActive)).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Returns a full Game System Definition by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(GameSystemDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameSystem(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await _registry.GetByIdAsync(id, cancellationToken);
            return Ok(MapToDto(definition));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Creates a new custom Game System Definition. Validates before persisting.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(GameSystemDefinitionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGameSystem(
        [FromBody] CreateGameSystemRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DefinitionJson))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "DefinitionJson is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Validate first
        using var validationStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.DefinitionJson));
        var validationResult = await _registry.ValidateAsync(validationStream, "json", cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Definition validation failed",
                Detail = string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")),
                Status = StatusCodes.Status400BadRequest,
            });
        }

        // Parse and create
        using var importStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.DefinitionJson));
        var definition = await _registry.ImportAsync(importStream, "json", cancellationToken);
        return CreatedAtAction(nameof(GetGameSystem), new { id = definition.Id }, MapToDto(definition));
    }

    /// <summary>
    /// Updates an existing Game System Definition. Creates a new version.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(GameSystemDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGameSystem(
        Guid id,
        [FromBody] UpdateGameSystemRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DefinitionJson))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "DefinitionJson is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Validate first
        using var validationStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.DefinitionJson));
        var validationResult = await _registry.ValidateAsync(validationStream, "json", cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Definition validation failed",
                Detail = string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")),
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var existing = await _registry.GetByIdAsync(id, cancellationToken);

            // Parse the new definition JSON to get the updated domain object
            using var parseStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.DefinitionJson));
            var parsed = await _registry.ImportAsync(parseStream, "json", cancellationToken);

            // Update creates a new version
            await _registry.UpdateAsync(id, parsed, cancellationToken);

            // Return the updated definition
            var updated = await _registry.GetByIdAsync(id, cancellationToken);
            return Ok(MapToDto(updated));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Deactivates a Game System Definition. Rejects if campaigns reference it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeactivateGameSystem(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _registry.DeactivateAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cannot deactivate",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    // ── Import/Export Endpoints ───────────────────────────────────────────────

    /// <summary>
    /// Imports a Game System Definition from an uploaded JSON or YAML file.
    /// </summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GameSystemDefinitionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportGameSystem(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "A definition file is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        var format = DetermineFormat(file.FileName, file.ContentType);
        if (format is null)
            return BadRequest(new ProblemDetails
            {
                Title = "Unsupported format",
                Detail = "Only JSON (.json) and YAML (.yaml, .yml) files are supported.",
                Status = StatusCodes.Status400BadRequest,
            });

        try
        {
            await using var stream = file.OpenReadStream();
            var definition = await _registry.ImportAsync(stream, format, cancellationToken);
            return CreatedAtAction(nameof(GetGameSystem), new { id = definition.Id }, MapToDto(definition));
        }
        catch (FluentValidation.ValidationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Definition validation failed",
                Detail = string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")),
                Status = StatusCodes.Status400BadRequest,
            });
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Parse error",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Exports a Game System Definition as JSON or YAML.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportGameSystem(
        Guid id,
        [FromQuery] string format = "json",
        CancellationToken cancellationToken = default)
    {
        if (format != "json" && format != "yaml")
            return BadRequest(new ProblemDetails
            {
                Title = "Unsupported format",
                Detail = "Supported formats are 'json' and 'yaml'.",
                Status = StatusCodes.Status400BadRequest,
            });

        try
        {
            var stream = await _registry.ExportAsync(id, format, cancellationToken);
            var definition = await _registry.GetByIdAsync(id, cancellationToken);
            var fileName = $"{definition.Identifier}.{format}";
            var contentType = format == "json" ? "application/json" : "application/x-yaml";

            return File(stream, contentType, fileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Validates a Game System Definition without saving it.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateGameSystem(
        [FromBody] ValidateGameSystemRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DefinitionJson))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "DefinitionJson is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.DefinitionJson));
        var result = await _registry.ValidateAsync(stream, "json", cancellationToken);

        return Ok(new ValidationResultDto(
            IsValid: result.IsValid,
            Errors: result.Errors.Select(e => new ValidationErrorDto(
                PropertyName: e.PropertyName,
                ErrorMessage: e.ErrorMessage)).ToList()));
    }

    // ── Editor Preview Endpoints ─────────────────────────────────────────────

    /// <summary>
    /// Lists starter templates for common TTRPG archetypes.
    /// </summary>
    [HttpGet("templates")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<GameSystemTemplateDto>), StatusCodes.Status200OK)]
    public IActionResult ListTemplates()
    {
        var templates = Aircane.Infrastructure.GameSystems.Seeds.StarterTemplates.GetAll();
        return Ok(templates);
    }

    /// <summary>
    /// Live preview of a dice convention — rolls dice using the definition's convention.
    /// </summary>
    [HttpPost("{id:guid}/preview-roll")]
    [ProducesResponseType(typeof(PreviewRollResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewRoll(
        Guid id,
        [FromBody] PreviewRollRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Formula))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Formula is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        GameSystemDefinition definition;
        try
        {
            definition = await _registry.GetByIdAsync(id, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        // Find the convention to use
        var conventionName = request.ConventionName ?? "primary";
        var convention = definition.DiceConventions.FirstOrDefault(c =>
            c.Name.Equals(conventionName, StringComparison.OrdinalIgnoreCase));

        if (convention is null)
        {
            convention = definition.DiceConventions.FirstOrDefault();
            if (convention is null)
                return BadRequest(new ProblemDetails
                {
                    Title = "No dice convention",
                    Detail = "This definition has no dice conventions configured.",
                    Status = StatusCodes.Status400BadRequest,
                });
        }

        // Parse the expression
        var parseResult = _mechanicResolver.ParseExpression(request.Formula, convention);
        if (!parseResult.IsSuccess)
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid dice expression",
                Detail = parseResult.Error?.Expected ?? "Failed to parse dice expression.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Roll using a real random source
        var random = new DefaultRandomSource();
        var rollResult = _mechanicResolver.Roll(parseResult.Expression!, convention, random);

        return Ok(new PreviewRollResponse(
            Formula: request.Formula,
            RawResults: rollResult.RawResults,
            KeptResults: rollResult.KeptResults,
            Modifier: rollResult.Modifier,
            Total: rollResult.Total,
            SuccessCount: rollResult.SuccessCount,
            OutcomeTier: rollResult.OutcomeTier,
            ExplodedResults: rollResult.ExplodedResults));
    }

    /// <summary>
    /// Preview character schema rendering — generates a form descriptor from the definition's character schema.
    /// </summary>
    [HttpPost("{id:guid}/preview-character-form")]
    [ProducesResponseType(typeof(FormDescriptor), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewCharacterForm(
        Guid id,
        [FromBody] PreviewCharacterFormRequest? request,
        CancellationToken cancellationToken)
    {
        GameSystemDefinition definition;
        try
        {
            definition = await _registry.GetByIdAsync(id, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        var schema = definition.CharacterSchema;
        if (schema is null)
            return Ok(new FormDescriptor { Sections = [] });

        var formDescriptor = _schemaEngine.GenerateFormDescriptor(schema);
        return Ok(formDescriptor);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private static GameSystemDefinitionDto MapToDto(GameSystemDefinition definition)
    {
        return new GameSystemDefinitionDto(
            Id: definition.Id,
            Identifier: definition.Identifier,
            Name: definition.Name,
            Version: definition.Version,
            SchemaVersion: definition.SchemaVersion,
            Publisher: definition.Publisher,
            Genre: definition.Genre,
            Description: definition.Description,
            License: definition.License,
            Tags: definition.Tags,
            IsActive: definition.IsActive,
            IsBuiltIn: definition.IsBuiltIn,
            DefinitionJson: definition.DefinitionJson,
            CreatedAt: definition.CreatedAt,
            UpdatedAt: definition.UpdatedAt);
    }

    private static string? DetermineFormat(string fileName, string contentType)
    {
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return "json";

        if (fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase))
            return "yaml";

        return null;
    }


}

// ── Supporting DTOs ──────────────────────────────────────────────────────────

/// <summary>
/// Result of a validation operation.
/// </summary>
public sealed record ValidationResultDto(
    bool IsValid,
    IReadOnlyList<ValidationErrorDto> Errors);

/// <summary>
/// A single validation error.
/// </summary>
public sealed record ValidationErrorDto(
    string PropertyName,
    string ErrorMessage);
