using Aircane.Api.Authorization;
using Aircane.Api.Extensions;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Api.Controllers;

/// <summary>
/// Adventure generation and management endpoints.
/// </summary>
[ApiController]
[Route("api/adventures")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public sealed class AdventuresController : ControllerBase
{
    private readonly IValidator<GenerateAdventureRequest> _validator;
    private readonly IAdventureGenerationService _generationService;
    private readonly IAdventureRetrievalService _retrievalService;
    private readonly IAdventureIndexingService _adventureIndexingService;
    private readonly IPartyAnalysisService _partyAnalysisService;
    private readonly AircaneDbContext _dbContext;
    private readonly ILogger<AdventuresController> _logger;

    public AdventuresController(
        IValidator<GenerateAdventureRequest> validator,
        IAdventureGenerationService generationService,
        IAdventureRetrievalService retrievalService,
        IAdventureIndexingService adventureIndexingService,
        IPartyAnalysisService partyAnalysisService,
        AircaneDbContext dbContext,
        ILogger<AdventuresController> logger)
    {
        _validator = validator;
        _generationService = generationService;
        _retrievalService = retrievalService;
        _adventureIndexingService = adventureIndexingService;
        _partyAnalysisService = partyAnalysisService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lists all adventures with optional status filter.
    /// Returns an empty array when no adventures exist.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdventureListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAdventures(
        [FromQuery] GeneratedAdventureStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.GeneratedAdventures.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        var adventures = await query
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AdventureListItemDto
            {
                Id = a.Id,
                Title = a.Title,
                Status = a.Status.ToString(),
                Ruleset = a.Ruleset,
                GameSystem = a.GameSystem,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(adventures);
    }

    /// <summary>
    /// Deletes a generated adventure and its indexed chunks.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAdventure(Guid id, CancellationToken cancellationToken)
    {
        var adventure = await _dbContext.GeneratedAdventures
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (adventure is null)
            return NotFound();

        // Find the associated SourceDocument (created during indexing)
        var sourceDocument = await _dbContext.SourceDocuments
            .FirstOrDefaultAsync(sd => sd.SourcePath == $"generated://{id}", cancellationToken);

        if (sourceDocument is not null)
        {
            // Remove all indexed chunks for this source document
            var chunks = await _dbContext.DocumentChunks
                .Where(c => c.SourceDocumentId == sourceDocument.Id)
                .ToListAsync(cancellationToken);

            _dbContext.DocumentChunks.RemoveRange(chunks);
            _dbContext.SourceDocuments.Remove(sourceDocument);
        }

        _dbContext.GeneratedAdventures.Remove(adventure);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted adventure {AdventureId} and its indexed chunks", id);

        return NoContent();
    }

    /// <summary>
    /// Retrieves a generated adventure by ID with all deserialized content.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GeneratedAdventureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdventure(Guid id, CancellationToken cancellationToken)
    {
        var adventure = await _retrievalService.GetByIdAsync(id, cancellationToken);
        if (adventure is null)
            return NotFound();

        return Ok(adventure);
    }

    /// <summary>
    /// Retrieves the draft sections of a generated adventure for review.
    /// Returns only the content sections without full request/party metadata.
    /// </summary>
    [HttpGet("{id:guid}/draft")]
    [ProducesResponseType(typeof(AdventureDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdventureDraft(Guid id, CancellationToken cancellationToken)
    {
        var draft = await _retrievalService.GetDraftAsync(id, cancellationToken);
        if (draft is null)
            return NotFound();

        return Ok(draft);
    }

    /// <summary>
    /// Starts an adventure generation pipeline. Runs the full staged generation
    /// and returns the generated adventure draft.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(GeneratedAdventureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateAdventure(
        [FromBody] GenerateAdventureRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.ToValidationProblemDetails());
        }

        _logger.LogInformation(
            "Adventure generation requested. Mode={Mode}, Ruleset={Ruleset}, PartySize={PartySize}, Level={Level}",
            request.Mode, request.Ruleset, request.PartySize, request.AverageLevel);

        // Run party analysis if character IDs are provided
        PartyAnalysisResult? partyAnalysis = null;
        if (request.CharacterIds is { Count: > 0 })
        {
            try
            {
                partyAnalysis = await _partyAnalysisService.AnalyzePartyAsync(
                    request.CharacterIds, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Party analysis failed, proceeding without it");
            }
        }

        // Run the full generation pipeline
        var result = await _generationService.GenerateAsync(request, partyAnalysis, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Approves a generated adventure draft, promoting it to playable status.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAdventure(Guid id, CancellationToken cancellationToken)
    {
        var adventure = await _dbContext.GeneratedAdventures
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (adventure is null)
            return NotFound();

        if (adventure.Status != GeneratedAdventureStatus.Draft)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cannot approve adventure",
                Detail = $"Adventure is in '{adventure.Status}' status and can only be approved from 'Draft' status.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        adventure.Status = GeneratedAdventureStatus.Approved;
        adventure.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Index the adventure so it's searchable by the RAG pipeline during play
        try
        {
            await _adventureIndexingService.IndexAdventureAsync(id, cancellationToken);
            _logger.LogInformation("Adventure {AdventureId} approved and indexed", id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Adventure {AdventureId} approved but indexing failed. It can be re-indexed later.", id);
        }

        return NoContent();
    }
}
