using Aircane.Api.Authorization;
using Aircane.Api.Extensions;
using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
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
    private readonly IAdventureRetrievalService _retrievalService;
    private readonly IAdventureIndexingService _adventureIndexingService;
    private readonly IBackgroundJobQueue _jobQueue;
    private readonly AircaneDbContext _dbContext;
    private readonly ILogger<AdventuresController> _logger;

    public AdventuresController(
        IValidator<GenerateAdventureRequest> validator,
        IAdventureRetrievalService retrievalService,
        IAdventureIndexingService adventureIndexingService,
        IBackgroundJobQueue jobQueue,
        AircaneDbContext dbContext,
        ILogger<AdventuresController> logger)
    {
        _validator = validator;
        _retrievalService = retrievalService;
        _adventureIndexingService = adventureIndexingService;
        _jobQueue = jobQueue;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lists adventures with optional filters. <c>status</c> narrows by status; <c>unassigned=true</c>
    /// returns only adventures that belong to no campaign, newest first (otherwise the order is
    /// most recently updated first). Returns an empty array when nothing matches.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdventureListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAdventures(
        [FromQuery] GeneratedAdventureStatus? status,
        CancellationToken cancellationToken = default,
        [FromQuery] bool unassigned = false)
    {
        var query = _dbContext.GeneratedAdventures.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (unassigned)
        {
            query = query.Where(a => a.CampaignId == null);
        }

        var ordered = unassigned
            ? query.OrderByDescending(a => a.CreatedAt)
            : query.OrderByDescending(a => a.UpdatedAt);

        var rows = await ordered
            .Select(a => new
            {
                a.Id, a.Title, a.Status, a.Ruleset, a.GameSystem, a.CampaignId,
                a.RequestJson, a.CreatedAt, a.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        // The in-progress run (if any), so the card can offer Resume instead of Play Solo.
        var active = await ActiveSessionsAsync(rows.Select(r => r.Id).ToList(), campaignId: null, cancellationToken);

        return Ok(rows.Select(r => ToListItem(
            r.Id, r.Title, r.Status, r.Ruleset, r.GameSystem, r.CampaignId, r.RequestJson, r.CreatedAt, r.UpdatedAt,
            active.GetValueOrDefault(r.Id))).ToList());
    }

    /// <summary>
    /// Assigns an adventure to a campaign (moving it off the unassigned list). Returns the updated
    /// adventure; 404 if the adventure or the campaign does not exist.
    /// </summary>
    [HttpPatch("{id:guid}/assign")]
    [ProducesResponseType(typeof(AdventureListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignCampaign(
        Guid id,
        [FromBody] AssignAdventureRequest request,
        CancellationToken cancellationToken)
    {
        var adventure = await _dbContext.GeneratedAdventures.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (adventure is null)
            return NotFound(new ProblemDetails { Title = "Adventure not found", Status = StatusCodes.Status404NotFound });

        if (!await _dbContext.Campaigns.AnyAsync(c => c.Id == request.CampaignId, cancellationToken))
            return NotFound(new ProblemDetails { Title = "Campaign not found", Status = StatusCodes.Status404NotFound });

        adventure.CampaignId = request.CampaignId;
        adventure.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Adventure {AdventureId} assigned to campaign {CampaignId}", id, request.CampaignId);

        return Ok(ToListItem(
            adventure.Id, adventure.Title, adventure.Status, adventure.Ruleset, adventure.GameSystem,
            adventure.CampaignId, adventure.RequestJson, adventure.CreatedAt, adventure.UpdatedAt));
    }

    /// <summary>
    /// Builds a list item. Mode / length / level live in the stored generation request rather than
    /// their own columns; an unreadable request just leaves them null.
    /// </summary>
    private static AdventureListItemDto ToListItem(
        Guid id, string title, GeneratedAdventureStatus status, string ruleset, string gameSystem,
        Guid? campaignId, string requestJson, DateTime createdAt, DateTime updatedAt,
        AdventureActiveSessionDto? activeSession = null)
    {
        GenerateAdventureRequest? request = null;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize<GenerateAdventureRequest>(
                requestJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            // leave the details null
        }

        return new AdventureListItemDto
        {
            Id = id,
            Title = title,
            Status = status.ToString(),
            Ruleset = ruleset,
            GameSystem = gameSystem,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CampaignId = campaignId,
            Mode = request?.Mode,
            Length = request?.Length,
            AverageLevel = request?.AverageLevel,
            ActiveSession = activeSession,
        };
    }

    /// <summary>
    /// The most recent in-progress run for each of the given adventures, with how far through it is.
    /// With a <paramref name="campaignId"/> only runs in that campaign count; without one, a run in any
    /// campaign does (an adventure that belongs to no campaign can still be played inside one).
    /// </summary>
    private async Task<Dictionary<Guid, AdventureActiveSessionDto>> ActiveSessionsAsync(
        List<Guid> adventureIds, Guid? campaignId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, AdventureActiveSessionDto>();
        if (adventureIds.Count == 0)
            return result;

        var runs = await _dbContext.AdventureSessions
            .AsNoTracking()
            .Where(s => s.Status == AdventureSessionStatus.InProgress
                        && adventureIds.Contains(s.GeneratedAdventureId)
                        && (campaignId == null || s.CampaignId == campaignId))
            .OrderByDescending(s => s.LastActivityAt)
            .Select(s => new { s.Id, s.GeneratedAdventureId, s.CurrentSceneId })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0)
            return result;

        var withRuns = runs.Select(r => r.GeneratedAdventureId).Distinct().ToList();
        var scenes = await _dbContext.GeneratedAdventures
            .AsNoTracking()
            .Where(a => withRuns.Contains(a.Id))
            .Select(a => new { a.Id, a.ScenesJson })
            .ToListAsync(cancellationToken);
        var sceneIdsByAdventure = scenes.ToDictionary(a => a.Id, a => ReadSceneIds(a.ScenesJson));

        foreach (var run in runs) // newest first, so the first run seen for an adventure is the one to show
        {
            if (result.ContainsKey(run.GeneratedAdventureId))
                continue;

            var sceneIds = sceneIdsByAdventure.GetValueOrDefault(run.GeneratedAdventureId) ?? [];
            var index = sceneIds.FindIndex(id => string.Equals(id, run.CurrentSceneId, StringComparison.OrdinalIgnoreCase));
            result[run.GeneratedAdventureId] = new AdventureActiveSessionDto(
                run.Id, index < 0 ? 1 : index + 1, sceneIds.Count, nameof(AdventureSessionStatus.InProgress));
        }

        return result;
    }

    /// <summary>The scene IDs, in order, from a stored scenes array.</summary>
    private static List<string> ReadSceneIds(string? scenesJson)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(scenesJson))
            return ids;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(scenesJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                return ids;

            foreach (var scene in doc.RootElement.EnumerateArray())
            {
                if (scene.ValueKind == System.Text.Json.JsonValueKind.Object
                    && scene.TryGetProperty("sceneId", out var id)
                    && id.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    ids.Add(id.GetString()!);
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // unreadable scenes: no progress figures
        }

        return ids;
    }

    /// <summary>
    /// Lists the adventures generated for a campaign, newest first.
    /// </summary>
    [HttpGet("/api/campaigns/{campaignId:guid}/adventures")]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignAdventureListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCampaignAdventures(Guid campaignId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Campaigns.AnyAsync(c => c.Id == campaignId, cancellationToken))
            return NotFound();

        var rows = await _dbContext.GeneratedAdventures
            .AsNoTracking()
            .Where(a => a.CampaignId == campaignId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Title, a.Status, a.GameSystem, a.Ruleset, a.RequestJson, a.CreatedAt })
            .ToListAsync(cancellationToken);

        var active = await ActiveSessionsAsync(rows.Select(r => r.Id).ToList(), campaignId, cancellationToken);

        // Mode / length / level live in the stored generation request, not in their own columns.
        var items = rows.Select(r =>
        {
            GenerateAdventureRequest? request = null;
            try
            {
                request = System.Text.Json.JsonSerializer.Deserialize<GenerateAdventureRequest>(
                    r.RequestJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException)
            {
                // Unreadable request: still list the adventure, just without those details.
            }

            return new CampaignAdventureListItemDto
            {
                Id = r.Id,
                Title = r.Title,
                Status = r.Status.ToString(),
                GameSystem = r.GameSystem,
                Ruleset = r.Ruleset,
                Mode = request?.Mode,
                Length = request?.Length,
                AverageLevel = request?.AverageLevel,
                CreatedAt = r.CreatedAt,
                ActiveSession = active.GetValueOrDefault(r.Id),
            };
        }).ToList();

        return Ok(items);
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
    /// Queues an adventure generation job and returns immediately. Progress and the result are
    /// pushed over SignalR (<c>AdventureGenerationProgress</c> / <c>Completed</c> / <c>Failed</c>);
    /// the finished adventure is then fetched with <c>GET /api/adventures/{id}</c>.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(GenerateAdventureResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateAdventure(
        [FromBody] GenerateAdventureRequest request,
        [FromQuery] Guid? campaignId,
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

        if (campaignId.HasValue && !await _dbContext.Campaigns.AnyAsync(c => c.Id == campaignId, cancellationToken))
            return NotFound(new ProblemDetails { Title = "Campaign not found", Status = StatusCodes.Status404NotFound });

        var job = new AdventureGenerationJobMessage(request, campaignId);
        await _jobQueue.EnqueueAsync(job, cancellationToken);

        return Accepted(new GenerateAdventureResponse(
            job.JobId, "Queued", "Adventure generation started."));
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
