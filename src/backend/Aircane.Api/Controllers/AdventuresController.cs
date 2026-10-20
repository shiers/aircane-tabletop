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
[Authorize(Policy = AuthorizationPolicies.SessionMember)]
public sealed class AdventuresController : ControllerBase
{
    private readonly IValidator<GenerateAdventureRequest> _validator;
    private readonly IAdventureRetrievalService _retrievalService;
    private readonly IAdventureIndexingService _adventureIndexingService;
    private readonly IBackgroundJobQueue _jobQueue;
    private readonly AircaneDbContext _dbContext;
    private readonly IGameSystemCanonicalizer _gameSystems;
    private readonly ILogger<AdventuresController> _logger;

    public AdventuresController(
        IValidator<GenerateAdventureRequest> validator,
        IAdventureRetrievalService retrievalService,
        IAdventureIndexingService adventureIndexingService,
        IBackgroundJobQueue jobQueue,
        AircaneDbContext dbContext,
        IGameSystemCanonicalizer gameSystems,
        ILogger<AdventuresController> logger)
    {
        _validator = validator;
        _retrievalService = retrievalService;
        _adventureIndexingService = adventureIndexingService;
        _jobQueue = jobQueue;
        _dbContext = dbContext;
        _gameSystems = gameSystems;
        _logger = logger;
    }

    /// <summary>
    /// Lists adventures with optional filters. <c>status</c> narrows by status; <c>unassigned=true</c>
    /// returns only adventures that belong to no campaign, newest first (otherwise the order is
    /// most recently updated first). Each item names its campaign (id and name) when it has one.
    /// Returns an empty array when nothing matches.
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
                CampaignName = _dbContext.Campaigns.Where(c => c.Id == a.CampaignId).Select(c => c.Name).FirstOrDefault(),
                a.RequestJson, a.CreatedAt, a.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        // The in-progress run (if any), so the card can offer Resume instead of Play Solo.
        var active = await ActiveSessionsAsync(rows.Select(r => r.Id).ToList(), campaignId: null, cancellationToken);

        return Ok(rows.Select(r => ToListItem(
            r.Id, r.Title, r.Status, r.Ruleset, r.GameSystem, r.CampaignId, r.RequestJson, r.CreatedAt, r.UpdatedAt,
            r.CampaignName, active.GetValueOrDefault(r.Id))).ToList());
    }

    /// <summary>
    /// Assigns an adventure to a campaign. Returns the updated adventure; 404 if the adventure or the
    /// campaign does not exist. Refused with 409 (a <c>code</c> in the problem body says why) when:
    /// <list type="bullet">
    ///   <item><c>adventure-run-in-progress</c>: the adventure belongs to another campaign and has a run in
    ///   progress. Never overridable.</item>
    ///   <item><c>adventure-in-other-campaign</c>: it belongs to another campaign (named in the body) and
    ///   <c>confirmMove</c> is not set.</item>
    ///   <item><c>game-system-mismatch</c>: the campaign's game system differs from the adventure's (both
    ///   named in the body) and <c>confirmMismatch</c> is not set.</item>
    /// </list>
    /// Assigning to the campaign it is already in changes nothing. Runs keep the campaign they were played in.
    /// </summary>
    [HttpPatch("{id:guid}/assign")]
    [ProducesResponseType(typeof(AdventureListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignCampaign(
        Guid id,
        [FromBody] AssignAdventureRequest request,
        CancellationToken cancellationToken)
    {
        var adventure = await _dbContext.GeneratedAdventures.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (adventure is null)
            return NotFound(new ProblemDetails { Title = "Adventure not found", Status = StatusCodes.Status404NotFound });

        var campaign = await _dbContext.Campaigns.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.GameSystem })
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
        if (campaign is null)
            return NotFound(new ProblemDetails { Title = "Campaign not found", Status = StatusCodes.Status404NotFound });

        if (adventure.CampaignId != campaign.Id)
        {
            if (adventure.CampaignId is { } otherId)
            {
                if (await HasRunInProgressAsync(id, cancellationToken))
                    return RunInProgress(adventure.Title, "moved to another campaign");

                if (!request.ConfirmMove)
                {
                    var otherName = await _dbContext.Campaigns.AsNoTracking()
                        .Where(c => c.Id == otherId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
                    return Conflict(Problem409(
                        "adventure-in-other-campaign",
                        "Adventure is in another campaign",
                        $"\"{adventure.Title}\" is in the campaign \"{otherName}\". Attaching it here takes it out of that campaign.",
                        ("currentCampaignId", otherId), ("currentCampaignName", otherName)));
                }
            }

            if (!request.ConfirmMismatch && !await SameGameSystemAsync(adventure.GameSystem, campaign.GameSystem, cancellationToken))
            {
                return Conflict(Problem409(
                    "game-system-mismatch",
                    "Game systems differ",
                    $"\"{adventure.Title}\" is for {adventure.GameSystem}, but the campaign \"{campaign.Name}\" uses {campaign.GameSystem}.",
                    ("adventureGameSystem", adventure.GameSystem), ("campaignGameSystem", campaign.GameSystem)));
            }

            adventure.CampaignId = campaign.Id;
            adventure.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Adventure {AdventureId} assigned to campaign {CampaignId}", id, campaign.Id);
        }

        return Ok(ToListItem(
            adventure.Id, adventure.Title, adventure.Status, adventure.Ruleset, adventure.GameSystem,
            adventure.CampaignId, adventure.RequestJson, adventure.CreatedAt, adventure.UpdatedAt, campaign.Name));
    }

    /// <summary>
    /// Takes an adventure out of its campaign. Only the link is cleared: the adventure, its indexed content and
    /// every run (with its history) stay, and the runs keep the campaign they were played in. 404 if the
    /// adventure does not exist; 409 (<c>adventure-run-in-progress</c>) while any run of it is in progress. An
    /// adventure that is in no campaign is left as it is.
    /// </summary>
    [HttpPatch("{id:guid}/unassign")]
    [ProducesResponseType(typeof(AdventureListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnassignCampaign(Guid id, CancellationToken cancellationToken)
    {
        var adventure = await _dbContext.GeneratedAdventures.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (adventure is null)
            return NotFound(new ProblemDetails { Title = "Adventure not found", Status = StatusCodes.Status404NotFound });

        if (adventure.CampaignId is not null)
        {
            if (await HasRunInProgressAsync(id, cancellationToken))
                return RunInProgress(adventure.Title, "taken out of its campaign");

            var campaignId = adventure.CampaignId;
            adventure.CampaignId = null;
            adventure.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Adventure {AdventureId} removed from campaign {CampaignId}", id, campaignId);
        }

        return Ok(ToListItem(
            adventure.Id, adventure.Title, adventure.Status, adventure.Ruleset, adventure.GameSystem,
            adventure.CampaignId, adventure.RequestJson, adventure.CreatedAt, adventure.UpdatedAt));
    }

    private Task<bool> HasRunInProgressAsync(Guid adventureId, CancellationToken cancellationToken)
        => _dbContext.AdventureSessions.AsNoTracking().AnyAsync(
            r => r.GeneratedAdventureId == adventureId && r.Status == AdventureSessionStatus.InProgress, cancellationToken);

    private ObjectResult RunInProgress(string title, string what) => Conflict(Problem409(
        "adventure-run-in-progress",
        "Adventure is being played",
        $"\"{title}\" has a run in progress, so it cannot be {what}. Finish or abandon the run first."));

    private static ProblemDetails Problem409(string code, string title, string detail, params (string Key, object? Value)[] extra)
    {
        var problem = new ProblemDetails { Title = title, Detail = detail, Status = StatusCodes.Status409Conflict };
        problem.Extensions["code"] = code;
        foreach (var (key, value) in extra)
            problem.Extensions[key] = value;
        return problem;
    }

    /// <summary>
    /// Whether an adventure's game system and a campaign's name the same system. Both are free text, so each is
    /// canonicalized against the known game system definitions first (name or identifier, ignoring case and
    /// surrounding space), then compared ignoring case; the campaign's known aliases count as the same too.
    /// When either side is blank there is nothing to compare, so it is not a mismatch.
    /// </summary>
    private async Task<bool> SameGameSystemAsync(string adventureSystem, string campaignSystem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adventureSystem) || string.IsNullOrWhiteSpace(campaignSystem))
            return true;

        var adventureName = await _gameSystems.CanonicalizeAsync(adventureSystem, cancellationToken);
        var campaignName = await _gameSystems.CanonicalizeAsync(campaignSystem, cancellationToken);
        if (string.Equals(adventureName.Trim(), campaignName.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        var equivalents = await _gameSystems.GetEquivalentNamesAsync(campaignSystem, cancellationToken);
        return equivalents.Any(n => string.Equals(n.Trim(), adventureName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a list item. Mode / length / level live in the stored generation request rather than
    /// their own columns; an unreadable request just leaves them null.
    /// </summary>
    private static AdventureListItemDto ToListItem(
        Guid id, string title, GeneratedAdventureStatus status, string ruleset, string gameSystem,
        Guid? campaignId, string requestJson, DateTime createdAt, DateTime updatedAt,
        string? campaignName = null, AdventureActiveSessionDto? activeSession = null)
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
            CampaignName = campaignName,
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
    /// What deleting an adventure would remove: its runs by status, their transcript lines and its indexed
    /// content. 404 if the adventure does not exist.
    /// </summary>
    [HttpGet("{id:guid}/delete-impact")]
    [ProducesResponseType(typeof(AdventureDeleteImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeleteImpact(Guid id, CancellationToken cancellationToken)
    {
        var title = await _dbContext.GeneratedAdventures.AsNoTracking()
            .Where(a => a.Id == id).Select(a => a.Title).FirstOrDefaultAsync(cancellationToken);
        if (title is null)
            return NotFound();

        var runs = await _dbContext.AdventureSessions.AsNoTracking()
            .Where(r => r.GeneratedAdventureId == id)
            .Select(r => new { r.Id, r.Status })
            .ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToList();

        var entries = runIds.Count == 0
            ? 0
            : await _dbContext.AdventureSessionEntries.AsNoTracking()
                .CountAsync(e => runIds.Contains(e.AdventureSessionId), cancellationToken);

        var documentId = await _dbContext.SourceDocuments.AsNoTracking()
            .Where(d => d.SourcePath == $"generated://{id}").Select(d => (Guid?)d.Id).FirstOrDefaultAsync(cancellationToken);
        var chunks = documentId is null
            ? 0
            : await _dbContext.DocumentChunks.AsNoTracking().CountAsync(c => c.SourceDocumentId == documentId, cancellationToken);

        return Ok(new AdventureDeleteImpactDto
        {
            Id = id,
            Title = title,
            TotalRuns = runs.Count,
            NotStartedRuns = runs.Count(r => r.Status == AdventureSessionStatus.NotStarted),
            InProgressRuns = runs.Count(r => r.Status == AdventureSessionStatus.InProgress),
            CompletedRuns = runs.Count(r => r.Status == AdventureSessionStatus.Completed),
            AbandonedRuns = runs.Count(r => r.Status == AdventureSessionStatus.Abandoned),
            TranscriptEntries = entries,
            IndexedChunks = chunks,
        });
    }

    /// <summary>
    /// Deletes a generated adventure for good: its indexed content, and every run of it with the transcripts.
    /// 404 if it does not exist; 409 (<c>adventure-run-in-progress</c>) while any run of it is in progress.
    /// <c>GET .../delete-impact</c> says what goes.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAdventure(Guid id, CancellationToken cancellationToken)
    {
        var adventure = await _dbContext.GeneratedAdventures
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (adventure is null)
            return NotFound();

        if (await HasRunInProgressAsync(id, cancellationToken))
            return RunInProgress(adventure.Title, "deleted");

        // The runs and their transcripts go with the adventure (the database cascades; removing them here too
        // keeps that true for a store that does not).
        var runs = await _dbContext.AdventureSessions.Where(r => r.GeneratedAdventureId == id).ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToList();
        if (runIds.Count > 0)
        {
            var entries = await _dbContext.AdventureSessionEntries
                .Where(e => runIds.Contains(e.AdventureSessionId)).ToListAsync(cancellationToken);
            _dbContext.AdventureSessionEntries.RemoveRange(entries);
            _dbContext.AdventureSessions.RemoveRange(runs);
        }

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

        _logger.LogInformation(
            "Deleted adventure {AdventureId}, its {RunCount} run(s) and its indexed chunks", id, runs.Count);

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
