using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Campaigns;

/// <summary>
/// EF Core-backed implementation of <see cref="ICampaignStateService"/>.
/// Maintains campaign state snapshots, applies commands, appends events,
/// supports undo, and controls reveal state.
/// </summary>
public sealed class CampaignStateService : ICampaignStateService
{
    private readonly AircaneDbContext _db;
    private readonly ILogger<CampaignStateService> _logger;

    public CampaignStateService(AircaneDbContext db, ILogger<CampaignStateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CampaignStateDto> LoadStateAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

        var snapshot = await _db.GameStateSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CampaignId == campaignId, cancellationToken);

        if (snapshot is not null)
        {
            return ToDto(snapshot);
        }

        // No snapshot exists yet - return a default empty state
        return new CampaignStateDto(
            CampaignId: campaignId,
            ActiveSessionId: null,
            CurrentSceneJson: null,
            StateJson: BuildDefaultStateJson(),
            SnapshotAt: campaign.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<CampaignStateDto> ApplyCommandAsync(
        ApplyCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var campaignExists = await _db.Campaigns
            .AnyAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (!campaignExists)
            throw new KeyNotFoundException($"Campaign '{request.CampaignId}' not found.");

        // Load or create the snapshot
        var snapshot = await _db.GameStateSnapshots
            .FirstOrDefaultAsync(s => s.CampaignId == request.CampaignId, cancellationToken);

        var currentState = snapshot is not null
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(snapshot.StateJson)
              ?? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BuildDefaultStateJson())
              ?? new Dictionary<string, JsonElement>();

        // Parse the command payload and apply it to the state
        var commandPayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.PayloadJson)
            ?? new Dictionary<string, JsonElement>();

        // Record the before-state for undo support
        var beforeStateJson = JsonSerializer.Serialize(currentState);

        // Apply the command: merge payload fields into the current state
        ApplyCommandToState(currentState, request.CommandType, commandPayload);

        var newStateJson = JsonSerializer.Serialize(currentState);

        // Extract currentSceneId if present
        string? currentSceneId = null;
        if (currentState.TryGetValue("currentSceneId", out var sceneElement) &&
            sceneElement.ValueKind == JsonValueKind.String)
        {
            currentSceneId = sceneElement.GetString();
        }

        // Upsert the snapshot
        if (snapshot is null)
        {
            snapshot = new GameStateSnapshot(
                campaignId: request.CampaignId,
                stateJson: newStateJson,
                activeSessionId: request.SessionId,
                currentSceneId: currentSceneId);
            _db.GameStateSnapshots.Add(snapshot);
        }
        else
        {
            snapshot.StateJson = newStateJson;
            snapshot.ActiveSessionId = request.SessionId ?? snapshot.ActiveSessionId;
            snapshot.CurrentSceneId = currentSceneId;
            snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Append a reversible event to the log
        var eventPayload = JsonSerializer.Serialize(new
        {
            commandType = request.CommandType,
            before = beforeStateJson,
            after = newStateJson,
            payload = request.PayloadJson
        });

        var campaignEvent = new CampaignEvent(
            campaignId: request.CampaignId,
            actorType: request.ActorType,
            eventType: $"Command:{request.CommandType}",
            payloadJson: eventPayload,
            reversible: true,
            sessionId: request.SessionId,
            actorId: request.ActorId);

        _db.CampaignEvents.Add(campaignEvent);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Applied command '{CommandType}' to campaign {CampaignId}",
            request.CommandType, request.CampaignId);

        return ToDto(snapshot);
    }

    /// <inheritdoc />
    public async Task<CampaignEventDto> AppendEventAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Verify campaign exists
        var exists = await _db.Campaigns
            .AnyAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (!exists)
            throw new KeyNotFoundException($"Campaign '{request.CampaignId}' not found.");

        var campaignEvent = new CampaignEvent(
            campaignId: request.CampaignId,
            actorType: request.ActorType,
            eventType: request.EventType,
            payloadJson: request.PayloadJson,
            reversible: request.Reversible,
            sessionId: request.SessionId,
            actorId: request.ActorId);

        _db.CampaignEvents.Add(campaignEvent);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Appended event '{EventType}' to campaign {CampaignId}",
            request.EventType, request.CampaignId);

        return ToEventDto(campaignEvent);
    }

    /// <inheritdoc />
    public async Task<CampaignStateDto> UndoLastCommandAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaignExists = await _db.Campaigns
            .AnyAsync(c => c.Id == campaignId, cancellationToken);

        if (!campaignExists)
            throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

        // Find the most recent reversible command event
        var lastReversible = await _db.CampaignEvents
            .Where(e => e.CampaignId == campaignId && e.Reversible && e.EventType.StartsWith("Command:"))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastReversible is null)
            throw new InvalidOperationException("No reversible commands to undo.");

        // Parse the before-state from the event payload
        var eventPayload = JsonSerializer.Deserialize<JsonElement>(lastReversible.PayloadJson);
        string? beforeStateJson = null;

        if (eventPayload.TryGetProperty("before", out var beforeElement))
        {
            beforeStateJson = beforeElement.GetString();
        }

        if (string.IsNullOrEmpty(beforeStateJson))
            throw new InvalidOperationException("Cannot undo: before-state not available in event payload.");

        // Restore the snapshot to the before-state
        var snapshot = await _db.GameStateSnapshots
            .FirstOrDefaultAsync(s => s.CampaignId == campaignId, cancellationToken);

        string? currentSceneId = null;
        var restoredState = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(beforeStateJson);
        if (restoredState is not null &&
            restoredState.TryGetValue("currentSceneId", out var sceneElement) &&
            sceneElement.ValueKind == JsonValueKind.String)
        {
            currentSceneId = sceneElement.GetString();
        }

        if (snapshot is null)
        {
            snapshot = new GameStateSnapshot(
                campaignId: campaignId,
                stateJson: beforeStateJson,
                currentSceneId: currentSceneId);
            _db.GameStateSnapshots.Add(snapshot);
        }
        else
        {
            snapshot.StateJson = beforeStateJson;
            snapshot.CurrentSceneId = currentSceneId;
            snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Remove the undone event from the log (mark as non-reversible)
        _db.CampaignEvents.Remove(lastReversible);

        // Append an undo event
        var undoEvent = new CampaignEvent(
            campaignId: campaignId,
            actorType: "System",
            eventType: "Undo",
            payloadJson: JsonSerializer.Serialize(new { undoneEventId = lastReversible.Id }),
            reversible: false);

        _db.CampaignEvents.Add(undoEvent);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Undid command event {EventId} for campaign {CampaignId}",
            lastReversible.Id, campaignId);

        return ToDto(snapshot);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignEventDto>> GetEventLogAsync(
        Guid campaignId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Campaigns
            .AnyAsync(c => c.Id == campaignId, cancellationToken);

        if (!exists)
            throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

        var events = await _db.CampaignEvents
            .AsNoTracking()
            .Where(e => e.CampaignId == campaignId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return events.Select(ToEventDto).ToList();
    }

    /// <inheritdoc />
    public async Task<CampaignStateDto> ReplayEventsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

        // Load all command events in chronological order (oldest first)
        var commandEvents = await _db.CampaignEvents
            .AsNoTracking()
            .Where(e => e.CampaignId == campaignId && e.EventType.StartsWith("Command:"))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        // Start from the default empty state and replay each command
        var state = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BuildDefaultStateJson())
            ?? new Dictionary<string, JsonElement>();

        foreach (var evt in commandEvents)
        {
            var eventPayload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);

            // Extract the original command payload from the event
            if (eventPayload.TryGetProperty("payload", out var payloadElement))
            {
                var payloadStr = payloadElement.GetString();
                if (!string.IsNullOrEmpty(payloadStr))
                {
                    var commandPayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadStr)
                        ?? new Dictionary<string, JsonElement>();

                    // Extract the command type from the event type (e.g., "Command:MoveScene" -> "MoveScene")
                    var commandType = evt.EventType.StartsWith("Command:")
                        ? evt.EventType["Command:".Length..]
                        : evt.EventType;

                    ApplyCommandToState(state, commandType, commandPayload);
                }
            }
        }

        var replayedStateJson = JsonSerializer.Serialize(state);

        // Extract currentSceneId if present
        string? currentSceneId = null;
        if (state.TryGetValue("currentSceneId", out var sceneElement) &&
            sceneElement.ValueKind == JsonValueKind.String)
        {
            currentSceneId = sceneElement.GetString();
        }

        _logger.LogInformation(
            "Replayed {EventCount} command events for campaign {CampaignId}",
            commandEvents.Count, campaignId);

        return new CampaignStateDto(
            CampaignId: campaignId,
            ActiveSessionId: null,
            CurrentSceneJson: currentSceneId,
            StateJson: replayedStateJson,
            SnapshotAt: commandEvents.Count > 0
                ? commandEvents[^1].CreatedAt
                : campaign.CreatedAt);
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private static void ApplyCommandToState(
        Dictionary<string, JsonElement> state,
        string commandType,
        Dictionary<string, JsonElement> payload)
    {
        switch (commandType)
        {
            case "MoveScene":
                ApplyMoveScene(state, payload);
                break;
            case "UpdatePartyResources":
                ApplyUpdatePartyResources(state, payload);
                break;
            case "SetWorldFlag":
                ApplySetWorldFlag(state, payload);
                break;
            case "RevealContent":
                ApplyRevealContent(state, payload);
                break;
            default:
                ApplyGenericMerge(state, payload);
                break;
        }
    }

    private static void ApplyMoveScene(
        Dictionary<string, JsonElement> state,
        Dictionary<string, JsonElement> payload)
    {
        if (payload.TryGetValue("sceneId", out var sceneId))
            state["currentSceneId"] = sceneId;
    }

    private static void ApplyUpdatePartyResources(
        Dictionary<string, JsonElement> state,
        Dictionary<string, JsonElement> payload)
    {
        if (payload.TryGetValue("resources", out var resources))
            state["partyResources"] = resources;
    }

    private static void ApplySetWorldFlag(
        Dictionary<string, JsonElement> state,
        Dictionary<string, JsonElement> payload)
    {
        if (payload.TryGetValue("key", out var flagKey) &&
            payload.TryGetValue("value", out var flagValue))
        {
            var flags = state.TryGetValue("worldFlags", out var existingFlags)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingFlags.GetRawText())
                  ?? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>();

            flags[flagKey.GetString()!] = flagValue;
            state["worldFlags"] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(flags));
        }
    }

    private static void ApplyRevealContent(
        Dictionary<string, JsonElement> state,
        Dictionary<string, JsonElement> payload)
    {
        if (payload.TryGetValue("contentId", out var contentId))
        {
            var revealed = state.TryGetValue("revealedContentIds", out var existingRevealed)
                ? JsonSerializer.Deserialize<List<string>>(existingRevealed.GetRawText())
                  ?? new List<string>()
                : new List<string>();

            var id = contentId.GetString()!;
            if (!revealed.Contains(id))
                revealed.Add(id);

            state["revealedContentIds"] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(revealed));
        }
    }

    private static void ApplyGenericMerge(
        Dictionary<string, JsonElement> state,
        Dictionary<string, JsonElement> payload)
    {
        foreach (var kvp in payload)
        {
            state[kvp.Key] = kvp.Value;
        }
    }

    private static string BuildDefaultStateJson()
    {
        var defaultState = new
        {
            currentSceneId = (string?)null,
            partyResources = new Dictionary<string, object>(),
            worldFlags = new Dictionary<string, object>(),
            revealedContentIds = new List<string>(),
            npcStates = new Dictionary<string, object>(),
            activeEncounterId = (string?)null
        };

        return JsonSerializer.Serialize(defaultState);
    }

    private static CampaignStateDto ToDto(GameStateSnapshot snapshot) => new(
        CampaignId: snapshot.CampaignId,
        ActiveSessionId: snapshot.ActiveSessionId,
        CurrentSceneJson: snapshot.CurrentSceneId,
        StateJson: snapshot.StateJson,
        SnapshotAt: snapshot.UpdatedAt);

    private static CampaignEventDto ToEventDto(CampaignEvent e) => new(
        Id: e.Id,
        CampaignId: e.CampaignId,
        SessionId: e.SessionId,
        ActorType: e.ActorType,
        ActorId: e.ActorId,
        EventType: e.EventType,
        PayloadJson: e.PayloadJson,
        Reversible: e.Reversible,
        CreatedAt: e.CreatedAt);
}
