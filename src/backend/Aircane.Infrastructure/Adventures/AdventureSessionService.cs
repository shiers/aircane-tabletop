using System.Text;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.Adventures;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// EF Core-backed implementation of <see cref="IAdventureSessionService"/>.
/// Tracks a run through a generated adventure, appends a non-reversible audit event to the
/// campaign event log for every change, and broadcasts SignalR events to the run's play session.
/// </summary>
public sealed class AdventureSessionService : IAdventureSessionService
{
    private const string ActorType = "System";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AircaneDbContext _db;
    private readonly IAdventureRetrievalService _retrieval;
    private readonly ICampaignStateService _campaignState;
    private readonly ILogger<AdventureSessionService> _logger;
    private readonly ISessionHubNotifier? _hubNotifier;
    private readonly IAiProvider? _aiProvider;

    public AdventureSessionService(
        AircaneDbContext db,
        IAdventureRetrievalService retrieval,
        ICampaignStateService campaignState,
        ILogger<AdventureSessionService> logger,
        ISessionHubNotifier? hubNotifier = null,
        IAiProvider? aiProvider = null)
    {
        _aiProvider = aiProvider;
        _db = db;
        _retrieval = retrieval;
        _campaignState = campaignState;
        _logger = logger;
        _hubNotifier = hubNotifier;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AdventureSession> StartAdventureSessionAsync(
        Guid generatedAdventureId,
        Guid campaignId,
        Guid? characterId,
        CancellationToken ct = default)
    {
        var adventure = await _retrieval.GetByIdAsync(generatedAdventureId, ct)
            ?? throw new KeyNotFoundException($"Adventure '{generatedAdventureId}' not found.");

        if (!await _db.Campaigns.AnyAsync(c => c.Id == campaignId, ct))
            throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

        // Idempotent: resuming is the same as starting.
        var existing = await _db.AdventureSessions
            .FirstOrDefaultAsync(s => s.CampaignId == campaignId && s.Status == AdventureSessionStatus.InProgress, ct);

        if (existing is not null)
        {
            if (existing.IsGroup)
            {
                throw new InvalidOperationException(
                    "This campaign already has a group adventure in progress. Use start-group to resume it, " +
                    "or abandon or complete it before starting a solo run.");
            }

            if (existing.GeneratedAdventureId == generatedAdventureId)
            {
                // Starting an adventure that is already running is a resume.
                await ClearPendingRollAsync(existing, ct);
                return existing;
            }


            throw new InvalidOperationException(
                "This campaign already has an adventure in progress. Abandon or complete it before starting another.");
        }

        RequirePlayable(adventure);

        if (characterId is not null && !await _db.Characters.AnyAsync(c => c.Id == characterId, ct))
            throw new KeyNotFoundException($"Character '{characterId}' not found.");

        var session = NewRun(adventure, generatedAdventureId, campaignId, characterId);

        _db.AdventureSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Started adventure session {AdventureSessionId} for adventure {AdventureId} in campaign {CampaignId}",
            session.Id, generatedAdventureId, campaignId);

        await AppendEventAsync(session, "Adventure:Started",
            new { adventureSessionId = session.Id, adventureId = generatedAdventureId, sceneId = session.CurrentSceneId }, ct);

        return session;
    }

    // ── Group play ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AdventureSession> StartGroupSessionAsync(
        Guid generatedAdventureId,
        Guid campaignId,
        IReadOnlyList<Guid> participantIds,
        Guid? playSessionId = null,
        TurnAdvanceMode turnAdvance = TurnAdvanceMode.ManualOnly,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participantIds);

        var adventure = await _retrieval.GetByIdAsync(generatedAdventureId, ct)
            ?? throw new KeyNotFoundException($"Adventure '{generatedAdventureId}' not found.");

        if (!await _db.Campaigns.AnyAsync(c => c.Id == campaignId, ct))
            throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

        var existing = await _db.AdventureSessions
            .FirstOrDefaultAsync(s => s.CampaignId == campaignId && s.Status == AdventureSessionStatus.InProgress, ct);

        if (existing is not null)
        {
            if (existing.GeneratedAdventureId == generatedAdventureId && existing.IsGroup)
            {
                // Idempotent, like a solo start: the turn order of the running group is left alone.
                await ClearPendingRollAsync(existing, ct);
                return existing;
            }

            throw new InvalidOperationException(existing.GeneratedAdventureId == generatedAdventureId
                ? "This campaign already has a solo run of this adventure in progress. Abandon or complete it before starting a group run."
                : "This campaign already has an adventure in progress. Abandon or complete it before starting another.");
        }

        RequirePlayable(adventure);

        if (participantIds.Count == 0)
            throw new InvalidOperationException("A group run needs at least one participant.");

        if (participantIds.Distinct().Count() != participantIds.Count)
            throw new InvalidOperationException("A participant can appear in the turn order only once.");

        var playSession = await ResolvePlaySessionAsync(campaignId, playSessionId, ct);

        var eligible = await FindActiveParticipantsAsync(playSession.Id, participantIds, ct);
        var ineligible = participantIds.Where(id => !eligible.ContainsKey(id)).ToList();
        if (ineligible.Count > 0)
        {
            throw new InvalidOperationException(
                $"Not active participants of the campaign's current session: {string.Join(", ", ineligible)}.");
        }

        var session = NewRun(adventure, generatedAdventureId, campaignId, characterId: null);
        session.SessionId = playSession.Id;
        session.IsGroup = true;
        session.TurnOrder = participantIds.ToList();
        session.ActiveParticipantId = participantIds[0];
        session.CurrentRound = 1;
        session.TurnAdvance = turnAdvance;

        _db.AdventureSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Started group adventure session {AdventureSessionId} for adventure {AdventureId} in campaign {CampaignId} with {PlayerCount} players",
            session.Id, generatedAdventureId, campaignId, participantIds.Count);

        await AppendEventAsync(session, "Adventure:Started",
            new
            {
                adventureSessionId = session.Id,
                adventureId = generatedAdventureId,
                sceneId = session.CurrentSceneId,
                isGroup = true,
                turnOrder = session.TurnOrder,
            }, ct);

        return session;
    }

    /// <summary>
    /// The play session a new group run belongs to: the one the host named, which must belong to the
    /// campaign and not have ended, or, when none was named, the campaign's most recent one that has not ended.
    /// </summary>
    private async Task<Session> ResolvePlaySessionAsync(Guid campaignId, Guid? playSessionId, CancellationToken ct)
    {
        if (playSessionId is not { } id)
        {
            return await _db.Sessions
                .AsNoTracking()
                .Where(s => s.CampaignId == campaignId && s.Status != SessionStatus.Ended)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    "This campaign has no play session in progress. Start a session and let the players join before starting a group run.");
        }

        var named = await _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException($"Play session '{id}' not found.");

        if (named.CampaignId != campaignId)
            throw new InvalidOperationException("That play session belongs to a different campaign.");

        if (named.Status == SessionStatus.Ended)
            throw new InvalidOperationException(
                "That play session has ended. Start a new session and let the players join before starting a group run.");

        return named;
    }

    /// <inheritdoc />
    public Task<AdventureSession> AdvanceTurnAsync(Guid sessionId, CancellationToken ct = default)
        => PassTurnAsync(sessionId, new TurnPass(TurnPassedBy.Host), ct);

    /// <summary>One lock per run: two requests to pass the same turn are handled one after the other.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> TurnLocks = new();

    /// <inheritdoc />
    public async Task<AdventureSession> PassTurnAsync(Guid sessionId, TurnPass pass, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pass);

        // The check ("is the turn still where the caller thinks?") and the move must not interleave with
        // another request's. Requests run in separate scopes and contexts, so the lock is static; this app is a
        // single process (the hub's connection registry is too). A second request waits, then sees the new state.
        var gate = TurnLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await PassTurnLockedAsync(sessionId, pass, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AdventureSession> PassTurnLockedAsync(Guid sessionId, TurnPass pass, CancellationToken ct)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        // The context may already hold this run from earlier in the request: read it again, under the lock.
        await _db.Entry(session).ReloadAsync(ct);
        RequireInProgress(session);
        RequireGroup(session);

        if (pass.ExpectedActiveParticipantId is { } expected && session.ActiveParticipantId != expected)
        {
            var nowNames = await LoadParticipantNamesAsync(session, ct);
            throw new TurnAlreadyMovedException(
                session.ActiveParticipantId,
                session.ActiveParticipantId is { } nowId ? NameOf(nowNames, nowId) : null);
        }

        var previousId = session.ActiveParticipantId;
        var previousIndex = previousId is { } p ? session.TurnOrder.IndexOf(p) : -1;
        var nextIndex = (previousIndex + 1) % session.TurnOrder.Count;

        // Passing the turn from the last participant back to the first starts a new round. When no
        // one holds the turn (previousIndex -1) the first participant simply picks it up.
        if (previousIndex >= 0 && nextIndex == 0)
            session.CurrentRound++;

        var nextId = session.TurnOrder[nextIndex];
        session.ActiveParticipantId = nextId;

        // A pending roll belongs to the turn it was asked in: only the acting player can answer it,
        // so once the host moves on it is dropped rather than read as the next player's result.
        session.PendingRollLabel = null;
        session.PendingRollDC = null;

        Touch(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Adventure session {AdventureSessionId} turn passed from {Previous} to {Next} (round {Round}) by {PassedBy} {PassedByParticipant}",
            sessionId, previousId, nextId, session.CurrentRound, pass.By, pass.PassedByParticipantId);

        // Who ended the turn is recorded with the event: the host, the acting player (their participant id),
        // or the run itself (auto-advance).
        await AppendEventAsync(session, "Adventure:TurnAdvanced",
            new
            {
                adventureSessionId = sessionId,
                from = previousId,
                to = nextId,
                round = session.CurrentRound,
                passedBy = pass.By.ToString(),
                passedByParticipantId = pass.PassedByParticipantId,
            }, ct);

        var names = await LoadParticipantNamesAsync(session, ct);
        await NotifyAsync(session, hub => hub.NotifyTurnAdvancedAsync(
            session.SessionId!.Value,
            new TurnAdvancedNotification(sessionId, previousId, nextId, NameOf(names, nextId), session.CurrentRound, pass.By.ToString()),
            ct));
        await NotifyStateChangedAsync(session.SessionId, sessionId, "TurnAdvanced", ct);

        return session;
    }

    /// <inheritdoc />
    public async Task<bool> AutoAdvanceAfterActionAsync(Guid sessionId, Guid actingParticipantId, CancellationToken ct = default)
    {
        // A fresh read: the run may have changed while the AI was answering.
        var run = await _db.AdventureSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (run is null || !run.IsGroup || run.Status != AdventureSessionStatus.InProgress
            || run.TurnAdvance != TurnAdvanceMode.AfterEachAction)
            return false;

        // The AI asked this player for a roll: their next message reports it, on the same turn. Passing now
        // would drop the roll (and hand the next player a result that is not theirs).
        if (!string.IsNullOrWhiteSpace(run.PendingRollLabel))
            return false;

        try
        {
            await PassTurnAsync(sessionId, new TurnPass(TurnPassedBy.Auto, ExpectedActiveParticipantId: actingParticipantId), ct);
            return true;
        }
        catch (TurnAlreadyMovedException)
        {
            return false;   // the host passed it while the AI was answering: nothing more to do
        }
        catch (InvalidOperationException)
        {
            return false;   // the run ended in the meantime (the AI completed the adventure)
        }
    }

    /// <inheritdoc />
    public async Task<AdventureSession> AddParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        RequireInProgress(session);
        RequireGroup(session);

        if (session.TurnOrder.Contains(participantId))
            throw new InvalidOperationException("That participant is already in this adventure run.");

        if (session.SessionId is not { } playSessionId)
            throw new InvalidOperationException("This adventure run has no play session for participants to join.");

        var eligible = await FindActiveParticipantsAsync(playSessionId, [participantId], ct);
        if (!eligible.TryGetValue(participantId, out var participantName))
            throw new InvalidOperationException($"Participant '{participantId}' is not an active participant of this run's play session.");

        session.TurnOrder.Add(participantId);
        Touch(session);
        await _db.SaveChangesAsync(ct);

        var position = session.TurnOrder.Count - 1;
        _logger.LogInformation(
            "Participant {ParticipantId} joined adventure session {AdventureSessionId} at turn position {Position}",
            participantId, sessionId, position);

        await AppendEventAsync(session, "Adventure:ParticipantJoined",
            new { adventureSessionId = sessionId, participantId, turnPosition = position /* zero-based */ }, ct);

        await NotifyAsync(session, hub => hub.NotifyParticipantJoinedAdventureAsync(
            playSessionId,
            new ParticipantJoinedAdventureNotification(sessionId, participantId, participantName, position),
            ct));

        return session;
    }

    /// <inheritdoc />
    public async Task AttachPlaySessionAsync(Guid sessionId, Guid playSessionId, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        session.SessionId = playSessionId;
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<AdventureSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.AdventureSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>How many of the latest exchanges the player view carries.</summary>
    private const int PlayerViewNarrationEntries = 20;

    /// <inheritdoc />
    public async Task<PlayerAdventureRunDto?> GetPlayerRunViewAsync(
        Guid playSessionId, Guid? callerParticipantId, CancellationToken ct = default)
    {
        var session = await _db.AdventureSessions
            .AsNoTracking()
            .Where(s => s.SessionId == playSessionId && s.Status == AdventureSessionStatus.InProgress)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);
        if (session is null)
            return null;

        var adventure = await LoadAdventureAsync(session, ct);
        var scenes = adventure.Scenes ?? [];
        var scene = FindScene(adventure, session.CurrentSceneId);
        var sceneNumber = scenes.ToList().FindIndex(s => SameId(s.SceneId, session.CurrentSceneId)) + 1;

        var turnOrder = session.IsGroup
            ? BuildTurnOrder(session, await LoadParticipantNamesAsync(session, ct))
            : [];
        var activeName = turnOrder.FirstOrDefault(t => t.IsActive)?.ParticipantName;

        // A pending roll is owed by whoever holds the turn (it is cleared when the turn passes), so only
        // that player is told about it.
        var pending = !string.IsNullOrWhiteSpace(session.PendingRollLabel)
                      && callerParticipantId is { } caller
                      && session.ActiveParticipantId == caller
            ? new PlayerPendingRollDto(session.PendingRollLabel!, session.PendingRollDC)
            : null;

        // The saved conversation is the public story: narration the AI DM gave the table and the actions
        // players typed. The AI's private DM note is never stored.
        var latest = await _db.AdventureSessionEntries
            .AsNoTracking()
            .Where(e => e.AdventureSessionId == session.Id)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.EntryType)
            .Take(PlayerViewNarrationEntries)
            .ToListAsync(ct);
        latest.Reverse();

        return new PlayerAdventureRunDto(
            RunId: session.Id,
            AdventureTitle: adventure.Pitch?.Title ?? "Untitled adventure",
            Status: session.Status.ToString(),
            IsGroup: session.IsGroup,
            // Only the read-aloud text: the scene's Description is a synopsis of what happens and can spoil it.
            Scene: new PlayerSceneDto(scene?.Title ?? "Unknown scene", NullIfBlank(scene?.ReadAloudText)),
            CurrentSceneNumber: sceneNumber,
            TotalScenes: scenes.Count,
            ActiveParticipantId: session.IsGroup ? session.ActiveParticipantId : null,
            ActiveParticipantName: activeName,
            CurrentRound: session.CurrentRound,
            TurnOrder: turnOrder,
            PendingRoll: pending,
            RecentNarration: latest
                .Select(e => new PlayerNarrationEntryDto(e.EntryType.ToString(), e.Content, e.CreatedAt))
                .ToList(),
            TurnAdvance: session.TurnAdvance);
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <inheritdoc />
    public Task<AdventureSession?> GetActiveByCampaignAsync(Guid campaignId, CancellationToken ct = default)
        => _db.AdventureSessions
            .AsNoTracking()
            .Where(s => s.CampaignId == campaignId && s.Status == AdventureSessionStatus.InProgress)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);

    // ── Reads ─────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AdventureSessionStateDto> GetSessionStateAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.AdventureSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new KeyNotFoundException($"Adventure session '{sessionId}' not found.");
        var adventure = await LoadAdventureAsync(session, ct);
        var names = await LoadParticipantNamesAsync(session, ct);

        return BuildState(session, adventure, names);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdventureExitDto>> GetAvailableScenesAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.AdventureSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new KeyNotFoundException($"Adventure session '{sessionId}' not found.");
        var adventure = await LoadAdventureAsync(session, ct);

        return ExitsOf(RequireScene(adventure, session.CurrentSceneId), adventure);
    }

    // ── State changes ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AdventureSession> AdvanceSceneAsync(
        Guid sessionId,
        string targetSceneId,
        string? transitionNarration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSceneId);

        var session = await LoadTrackedAsync(sessionId, ct);
        RequireInProgress(session);
        var adventure = await LoadAdventureAsync(session, ct);

        var current = RequireScene(adventure, session.CurrentSceneId);
        var target = FindScene(adventure, targetSceneId.Trim())
            ?? throw new InvalidOperationException($"Scene '{targetSceneId}' does not exist in this adventure.");

        if (!current.ConnectsTo.Any(id => SameId(id, target.SceneId)))
        {
            throw new InvalidOperationException(
                $"Scene '{target.SceneId}' is not reachable from the current scene '{current.SceneId}'.");
        }

        var previousSceneId = session.CurrentSceneId;
        if (!session.CompletedSceneIds.Contains(previousSceneId))
            session.CompletedSceneIds.Add(previousSceneId);

        session.CurrentSceneId = target.SceneId;
        Touch(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Adventure session {AdventureSessionId} advanced from {Previous} to {Target}",
            sessionId, previousSceneId, target.SceneId);

        await AppendEventAsync(session, "Adventure:SceneAdvanced",
            new { adventureSessionId = sessionId, from = previousSceneId, to = target.SceneId }, ct);

        await NotifyAsync(session, hub => hub.NotifyAdventureSceneAdvancedAsync(
            session.SessionId!.Value,
            new AdventureSceneAdvancedNotification(sessionId, previousSceneId, target.SceneId, transitionNarration),
            ct));
        await NotifyStateChangedAsync(session.SessionId, sessionId, "SceneAdvanced", ct);

        return session;
    }

    /// <inheritdoc />
    public async Task RevealClueAsync(Guid sessionId, string clueId, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        RequireInProgress(session);
        var adventure = await LoadAdventureAsync(session, ct);

        var clue = AdventureContentIds.ForClues(adventure.Clues).FirstOrDefault(c => SameId(c.Id, clueId));
        if (clue.Id is null)
            throw new InvalidOperationException($"Clue '{clueId}' does not exist in this adventure.");

        if (session.RevealedClueIds.Contains(clue.Id))
            return;

        session.RevealedClueIds.Add(clue.Id);
        Touch(session);
        await _db.SaveChangesAsync(ct);

        await AppendEventAsync(session, "Adventure:ClueRevealed",
            new { adventureSessionId = sessionId, clueId = clue.Id, name = clue.Handout.Title }, ct);

        await NotifyAsync(session, hub => hub.NotifyAdventureClueRevealedAsync(
            session.SessionId!.Value,
            new AdventureClueRevealedNotification(sessionId, clue.Id, clue.Handout.Title, clue.Handout.Content),
            ct));
    }

    /// <inheritdoc />
    public async Task RevealSecretAsync(Guid sessionId, string secretId, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        RequireInProgress(session);
        var adventure = await LoadAdventureAsync(session, ct);

        var secret = AdventureContentIds.ForSecrets(adventure.Clues).FirstOrDefault(s => SameId(s.Id, secretId));
        if (secret.Id is null)
            throw new InvalidOperationException($"Secret '{secretId}' does not exist in this adventure.");

        if (session.RevealedSecretIds.Contains(secret.Id))
            return;

        session.RevealedSecretIds.Add(secret.Id);
        Touch(session);
        await _db.SaveChangesAsync(ct);

        await AppendEventAsync(session, "Adventure:SecretRevealed",
            new { adventureSessionId = sessionId, secretId = secret.Id, name = secret.Secret.Title }, ct);

        await NotifyAsync(session, hub => hub.NotifyAdventureSecretRevealedAsync(
            session.SessionId!.Value,
            new AdventureSecretRevealedNotification(sessionId, secret.Id, secret.Secret.Title, secret.Secret.Content),
            ct));
    }

    /// <inheritdoc />
    public async Task UpdateNpcStateAsync(
        Guid sessionId,
        string npcId,
        bool? hasBeenMet,
        bool? isAlive,
        string? revealedInfoId,
        string? playerNotes,
        CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        RequireInProgress(session);

        var npc = session.NpcStates.FirstOrDefault(n => SameId(n.NpcId, npcId))
            ?? throw new InvalidOperationException($"NPC '{npcId}' does not exist in this adventure.");

        var changed = false;

        if (hasBeenMet is not null && npc.HasBeenMet != hasBeenMet.Value)
        {
            npc.HasBeenMet = hasBeenMet.Value;
            changed = true;
        }

        if (isAlive is not null && npc.IsAlive != isAlive.Value)
        {
            npc.IsAlive = isAlive.Value;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(revealedInfoId) && !npc.RevealedInfoIds.Contains(revealedInfoId))
        {
            npc.RevealedInfoIds.Add(revealedInfoId);
            changed = true;
        }

        if (playerNotes is not null && npc.PlayerNotes != playerNotes)
        {
            npc.PlayerNotes = playerNotes;
            changed = true;
        }

        if (!changed)
            return;

        Touch(session);
        await _db.SaveChangesAsync(ct);

        await AppendEventAsync(session, "Adventure:NpcUpdated",
            new { adventureSessionId = sessionId, npcId = npc.NpcId, npc.HasBeenMet, npc.IsAlive }, ct);

        await NotifyAsync(session, hub => hub.NotifyAdventureNpcUpdatedAsync(
            session.SessionId!.Value,
            new AdventureNpcUpdatedNotification(sessionId, npc.NpcId, npc.NpcName, npc.HasBeenMet, npc.IsAlive),
            ct));
    }

    /// <inheritdoc />
    public async Task UpdatePendingRollAsync(Guid sessionId, string? label, int? dc, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        var newLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        var newDc = newLabel is null ? null : dc;

        if (session.PendingRollLabel == newLabel && session.PendingRollDC == newDc)
            return;

        session.PendingRollLabel = newLabel;
        session.PendingRollDC = newDc;
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task ResumeAsync(Guid sessionId, CancellationToken ct = default)
        => await ClearPendingRollAsync(await LoadTrackedAsync(sessionId, ct), ct);

    private async Task ClearPendingRollAsync(AdventureSession session, CancellationToken ct)
    {
        if (session.PendingRollLabel is null && session.PendingRollDC is null)
            return;

        session.PendingRollLabel = null;
        session.PendingRollDC = null;
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<string> GenerateRecapAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_aiProvider is null)
            throw new InvalidOperationException("No AI provider is configured, so a recap cannot be generated.");

        var session = await _db.AdventureSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new KeyNotFoundException($"Adventure session '{sessionId}' not found.");
        var adventure = await LoadAdventureAsync(session, ct);

        // A short, focused task: just the run's state, no retrieval and none of the DM prompt.
        var reply = await _aiProvider.ChatCompletionAsync(
        [
            AiMessage.System("You write brief, vivid reminders for a tabletop role-playing player. Reply with the reminder text only."),
            AiMessage.User(BuildRecapPrompt(session, adventure)),
        ], ct);

        var recap = reply.Trim().Trim('"').Trim();
        if (recap.Length == 0)
            throw new InvalidOperationException("The AI returned an empty recap.");

        return recap;
    }

    /// <summary>The recap request: the instruction plus the facts of the run so far.</summary>
    public static string BuildRecapPrompt(AdventureSession session, GeneratedAdventureDto adventure)
    {
        var scenes = adventure.Scenes ?? [];
        var scene = FindScene(adventure, session.CurrentSceneId);

        string List(IEnumerable<string> items, string none)
        {
            var joined = string.Join(", ", items.Where(i => !string.IsNullOrWhiteSpace(i)));
            return joined.Length == 0 ? none : joined;
        }

        var completed = session.CompletedSceneIds
            .Select(id => scenes.FirstOrDefault(s => SameId(s.SceneId, id))?.Title ?? id);
        var clues = RevealedClues(session, adventure).Select(c => c.Name);
        var secrets = RevealedSecrets(session, adventure).Select(s => s.Name);
        var objectives = scene?.Objectives ?? [];

        return $"""
            Summarize what has happened in this adventure session so far in 2-3 sentences, written in second person present tense as if reminding the player. Include: which scenes have been completed, what clues or secrets have been revealed, the current situation, and what the player's immediate goal is. Be concise - this is a reminder, not a retelling.

            Adventure: {adventure.Pitch?.Title ?? "Untitled adventure"}
            Completed scenes: {List(completed, "none yet")}
            Revealed clues: {List(clues, "none yet")}
            Revealed secrets: {List(secrets, "none yet")}
            Current scene: {scene?.Title ?? session.CurrentSceneId} - {scene?.Description ?? "(unknown)"}
            Current objectives: {(objectives.Count == 0 ? "none listed" : string.Join("; ", objectives))}
            """;
    }

    /// <inheritdoc />
    public async Task RecordExchangeAsync(
        Guid sessionId,
        string playerAction,
        string narration,
        string? sceneIdAtStart,
        CancellationToken ct = default)
    {
        var run = await _db.AdventureSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.CurrentSceneId, s.SessionId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Adventure session '{sessionId}' not found.");
        var currentSceneId = run.CurrentSceneId;

        // Entries are read back by time, so keep it strictly increasing per run: the action before its
        // answer, and each exchange after the previous one, even when the clock has not ticked.
        var lastAt = await _db.AdventureSessionEntries
            .Where(e => e.AdventureSessionId == sessionId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => (DateTimeOffset?)e.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var now = DateTimeOffset.UtcNow;
        if (lastAt is { } last && now <= last)
            now = last.AddMilliseconds(1);

        _db.AdventureSessionEntries.Add(new AdventureSessionEntry
        {
            Id = Guid.NewGuid(),
            AdventureSessionId = sessionId,
            EntryType = AdventureEntryType.PlayerAction,
            Content = playerAction,
            SceneId = sceneIdAtStart ?? currentSceneId,
            CreatedAt = now,
        });
        _db.AdventureSessionEntries.Add(new AdventureSessionEntry
        {
            Id = Guid.NewGuid(),
            AdventureSessionId = sessionId,
            EntryType = AdventureEntryType.AiNarration,
            Content = narration,
            SceneId = currentSceneId,
            CreatedAt = now.AddMilliseconds(1),
        });

        await _db.SaveChangesAsync(ct);

        // The table can now read the answer: tell the other players to look (a signal only, no text).
        await NotifyStateChangedAsync(run.SessionId, sessionId, "PlayerAction", ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdventureHistoryEntryDto>> GetHistoryAsync(
        Guid sessionId, int limit = 50, CancellationToken ct = default)
    {
        if (!await _db.AdventureSessions.AnyAsync(s => s.Id == sessionId, ct))
            throw new KeyNotFoundException($"Adventure session '{sessionId}' not found.");

        // Newest N, then flipped to oldest-first for display.
        var latest = await _db.AdventureSessionEntries
            .AsNoTracking()
            .Where(e => e.AdventureSessionId == sessionId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.EntryType)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        latest.Reverse();
        return latest
            .Select(e => new AdventureHistoryEntryDto(e.Id, e.EntryType.ToString(), e.Content, e.SceneId, e.CreatedAt))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdventureSession>> ListInProgressAsync(CancellationToken ct = default)
        => await _db.AdventureSessions
            .AsNoTracking()
            .Where(s => s.Status == AdventureSessionStatus.InProgress)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task CompleteAdventureAsync(Guid sessionId, string outcomeNotes, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);
        RequireInProgress(session);

        if (!session.CompletedSceneIds.Contains(session.CurrentSceneId))
            session.CompletedSceneIds.Add(session.CurrentSceneId);

        session.Status = AdventureSessionStatus.Completed;
        session.OutcomeNotes = outcomeNotes;
        session.CompletedAt = DateTimeOffset.UtcNow;
        Touch(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Adventure session {AdventureSessionId} completed", sessionId);

        await AppendEventAsync(session, "Adventure:Completed",
            new { adventureSessionId = sessionId, outcomeNotes }, ct);

        await NotifyAsync(session, hub => hub.NotifyAdventureCompletedAsync(
            session.SessionId!.Value,
            new AdventureCompletedNotification(sessionId, outcomeNotes),
            ct));
        // The outcome notes above go to the host only; the table is told the run changed, and reads it.
        await NotifyStateChangedAsync(session.SessionId, sessionId, "Completed", ct);
    }

    /// <inheritdoc />
    public async Task AbandonAdventureAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadTrackedAsync(sessionId, ct);

        if (session.Status == AdventureSessionStatus.Abandoned)
            return;

        if (session.Status == AdventureSessionStatus.Completed)
            throw new InvalidOperationException("A completed adventure cannot be abandoned.");

        session.Status = AdventureSessionStatus.Abandoned;
        session.CompletedAt = DateTimeOffset.UtcNow;
        Touch(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Adventure session {AdventureSessionId} abandoned", sessionId);

        await AppendEventAsync(session, "Adventure:Abandoned", new { adventureSessionId = sessionId }, ct);
    }

    // ── AI context ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AdventureContext?> BuildActiveAdventureContextAsync(Guid campaignId, CancellationToken ct = default)
    {
        var session = await GetActiveByCampaignAsync(campaignId, ct);
        if (session is null)
            return null;

        var adventure = await _retrieval.GetByIdAsync(session.GeneratedAdventureId, ct);
        if (adventure?.Scenes is not { Count: > 0 })
            return null;

        // A group run also tells the AI who is playing, as which character, and whose turn it is.
        IReadOnlyList<ParticipantTurnDto>? turnOrder = null;
        IReadOnlyDictionary<Guid, string>? characterNames = null;
        List<string>? playersWithoutCharacter = null;
        if (session.IsGroup)
        {
            turnOrder = BuildTurnOrder(session, await LoadParticipantNamesAsync(session, ct));
            characterNames = await LoadCharacterNamesAsync(session, ct);
            playersWithoutCharacter = turnOrder
                .Where(t => !characterNames.ContainsKey(t.ParticipantId))
                .Select(t => t.ParticipantName)
                .ToList();
        }

        return new AdventureContext(
            session.Id,
            BuildAdventureContext(session, adventure, turnOrder, characterNames),
            session.CurrentSceneId,
            session.IsGroup,
            playersWithoutCharacter);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Knows only the run's participant IDs, not their names, so a group run's group-play block is
    /// left out. <see cref="BuildActiveAdventureContextAsync"/> includes it.
    /// </remarks>
    public string BuildAdventureContext(AdventureSession session, GeneratedAdventureDto adventure)
        => BuildAdventureContext(session, adventure, turnOrder: null);

    /// <summary>
    /// Renders the adventure context block; for a group run given its <paramref name="turnOrder"/>,
    /// also the group-play block (players and their characters, whose turn it is, the round).
    /// </summary>
    /// <param name="characterNames">Character name by participant ID, for participants who have one.</param>
    public string BuildAdventureContext(
        AdventureSession session,
        GeneratedAdventureDto adventure,
        IReadOnlyList<ParticipantTurnDto>? turnOrder,
        IReadOnlyDictionary<Guid, string>? characterNames = null)
    {
        var scenes = adventure.Scenes ?? [];
        var scene = FindScene(adventure, session.CurrentSceneId);
        var title = adventure.Pitch?.Title ?? "Untitled adventure";

        var sb = new StringBuilder();

        // First and unmissable: the player's next message answers this.
        if (!string.IsNullOrWhiteSpace(session.PendingRollLabel))
        {
            var dc = session.PendingRollDC is { } d ? $", DC {d}" : string.Empty;
            if (session.IsGroup)
            {
                // Passing the turn clears the roll, so it is always owed by whoever is acting now.
                var actor = turnOrder is { Count: > 0 }
                    ? ParticipantLabel(turnOrder.FirstOrDefault(t => t.IsActive) ?? turnOrder[0], characterNames)
                    : "the acting player";
                sb.AppendLine(
                    $"PENDING ROLL: Earlier this turn, {actor} was asked to roll {session.PendingRollLabel}{dc}. " +
                    $"The current message, from {actor}, is the result of that roll - do NOT ask them to clarify what the roll was for. " +
                    "Assume it is this pending roll. If the roll names several characters, the message reports each one's result: resolve each separately. " +
                    "Narrate the outcome of the roll before continuing the scene.");
            }
            else
            {
                sb.AppendLine(
                    $"PENDING ROLL: The player was previously asked to roll {session.PendingRollLabel}{dc}. " +
                    "Their current message is the result of that roll - do NOT ask them to clarify what the roll was for. " +
                    "Assume it is this pending roll. Narrate the outcome of the roll before continuing the scene.");
            }
            sb.AppendLine();
        }

        if (session.IsGroup && turnOrder is { Count: > 0 })
        {
            sb.AppendLine(BuildGroupContext(session, turnOrder, characterNames));
            sb.AppendLine();
        }

        sb.AppendLine($"Current Adventure: {title}");

        if (scene is null)
        {
            sb.AppendLine($"Current Scene: (unknown scene '{session.CurrentSceneId}')");
            AppendProgress(sb, session, scenes.Count);
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"Current Scene: {scene.Title} [{scene.SceneId}] ({scene.SceneType})");
        sb.AppendLine();
        sb.AppendLine(scene.Description);

        if (!string.IsNullOrWhiteSpace(scene.ReadAloudText))
        {
            sb.AppendLine();
            sb.AppendLine($"Read-aloud text (use when the player first arrives): {scene.ReadAloudText}");
        }

        if (!string.IsNullOrWhiteSpace(scene.DmNotes))
        {
            sb.AppendLine();
            sb.AppendLine($"DM notes (private, never quote to the player): {scene.DmNotes}");
        }

        sb.AppendLine();
        sb.AppendLine("Scene Objectives:");
        var objectives = scene.Objectives ?? [];
        if (objectives.Count == 0)
            sb.AppendLine("- (none listed - treat the scene as complete once its main situation is resolved)");
        foreach (var objective in objectives)
            sb.AppendLine($"- {objective}");

        sb.AppendLine();
        sb.AppendLine("Available Exits:");
        var exits = ExitsOf(scene, adventure);
        if (exits.Count == 0)
            sb.AppendLine("- (none - this is the final scene; complete the adventure when its objectives are met)");
        foreach (var exit in exits)
            sb.AppendLine($"- {exit.Title} [{exit.SceneId}]");

        AppendNpcs(sb, session, adventure, scene);
        AppendEncounters(sb, adventure, scene);
        AppendDiscoverable(sb, session, adventure, scene);
        AppendFailForward(sb, adventure, scene);

        sb.AppendLine();
        sb.AppendLine("Already Revealed:");
        var clueNames = RevealedClues(session, adventure).Select(c => c.Name).ToList();
        var secretNames = RevealedSecrets(session, adventure).Select(s => s.Name).ToList();
        sb.AppendLine($"Clues: {(clueNames.Count == 0 ? "(none)" : string.Join(", ", clueNames))}");
        sb.AppendLine($"Secrets: {(secretNames.Count == 0 ? "(none)" : string.Join(", ", secretNames))}");

        AppendProgress(sb, session, scenes.Count);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The group-play block of the adventure context: who is playing, as which character, whose turn
    /// it is, and the round. Facts only; how to run a table of players is in the group instruction
    /// block (<c>PlayerActionService.BuildGroupAdventureInstructions</c>).
    /// </summary>
    /// <remarks>
    /// Each participant is listed by character name with the player's name after it ("Mira (played
    /// by Alice)"), or by the player's name alone when they have no character in this app.
    /// </remarks>
    public static string BuildGroupContext(
        AdventureSession session,
        IReadOnlyList<ParticipantTurnDto> turnOrder,
        IReadOnlyDictionary<Guid, string>? characterNames = null)
    {
        string Label(ParticipantTurnDto t) => ParticipantLabel(t, characterNames);

        var active = turnOrder.FirstOrDefault(t => t.IsActive) ?? turnOrder[0];

        var sb = new StringBuilder();
        sb.AppendLine($"GROUP PLAY — {turnOrder.Count} players are in this session.");
        sb.AppendLine($"Turn order: {string.Join(", ", turnOrder.Select(Label))}.");
        sb.AppendLine($"Currently acting: {Label(active)}.");
        sb.Append($"Round: {session.CurrentRound}.");
        return sb.ToString();
    }

    /// <summary>"Mira (played by Alice)", or the player's name alone when they have no character.</summary>
    private static string ParticipantLabel(ParticipantTurnDto turn, IReadOnlyDictionary<Guid, string>? characterNames)
        => characterNames is not null && characterNames.TryGetValue(turn.ParticipantId, out var character)
            ? $"{character} (played by {turn.ParticipantName})"
            : turn.ParticipantName;

    private static void AppendProgress(StringBuilder sb, AdventureSession session, int totalScenes)
    {
        sb.AppendLine();
        sb.AppendLine($"Completed Scenes: {session.CompletedSceneIds.Count} of {totalScenes}");
    }

    private static void AppendNpcs(StringBuilder sb, AdventureSession session, GeneratedAdventureDto adventure, GeneratedScene scene)
    {
        var present = NpcsInScene(adventure, scene).ToList();
        sb.AppendLine();
        sb.AppendLine("NPCs Present:");
        if (present.Count == 0)
            sb.AppendLine("- (none)");

        foreach (var (id, npc) in present)
        {
            var state = session.NpcStates.FirstOrDefault(n => SameId(n.NpcId, id));
            var met = state?.HasBeenMet == true ? "already met" : "not yet met";
            var alive = state?.IsAlive == false ? "dead" : "alive";
            sb.Append($"- {npc.Name} [{id}] ({npc.Role}) - {alive}, {met}. Personality: {npc.Personality}");
            if (state is { RevealedInfoIds.Count: > 0 })
                sb.Append($" Info already revealed: {string.Join(", ", state.RevealedInfoIds)}.");
            if (!string.IsNullOrWhiteSpace(state?.PlayerNotes))
                sb.Append($" Notes: {state.PlayerNotes}");
            sb.AppendLine();
        }
    }

    private static void AppendEncounters(StringBuilder sb, GeneratedAdventureDto adventure, GeneratedScene scene)
    {
        var encounters = AdventureContentIds.ForEncounters(adventure.Encounters)
            .Where(e => SameId(e.Encounter.SceneId, scene.SceneId))
            .ToList();
        if (encounters.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Encounters In This Scene:");
        foreach (var (id, encounter) in encounters)
        {
            var enemies = string.Join(", ", encounter.Enemies.Select(e => $"{e.Count}x {e.Name} ({e.ChallengeRating})"));
            sb.AppendLine($"- {encounter.Title} [{id}] ({encounter.Difficulty}): {enemies}. Tactics: {encounter.Tactics}");
        }
    }

    private static void AppendDiscoverable(StringBuilder sb, AdventureSession session, GeneratedAdventureDto adventure, GeneratedScene scene)
    {
        var clues = AdventureContentIds.ForClues(adventure.Clues)
            .Where(c => SameId(c.Handout.SceneId, scene.SceneId) && !session.RevealedClueIds.Contains(c.Id))
            .ToList();
        var secrets = AdventureContentIds.ForSecrets(adventure.Clues)
            .Where(s => SameId(s.Secret.SceneId, scene.SceneId) && !session.RevealedSecretIds.Contains(s.Id))
            .ToList();

        if (clues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Clues Findable Here (not yet found - reveal with RevealClue only when the player finds them):");
            foreach (var (id, handout) in clues)
                sb.AppendLine($"- {handout.Title} [{id}]: {handout.Content}");
        }

        if (secrets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Secrets Hidden Here (DM only until discovered - reveal with RevealSecret only when the player uncovers them):");
            foreach (var (id, secret) in secrets)
                sb.AppendLine($"- {secret.Title} [{id}]: {secret.Content} (Discovery: {secret.DiscoveryMethod})");
        }
    }

    private static void AppendFailForward(StringBuilder sb, GeneratedAdventureDto adventure, GeneratedScene scene)
    {
        var paths = (adventure.Clues?.FailForwardPaths ?? [])
            .Where(p => SameId(p.SceneId, scene.SceneId))
            .ToList();
        if (paths.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("If The Player Gets Stuck:");
        foreach (var path in paths)
            sb.AppendLine($"- {path.Trigger} -> {path.Resolution}");
    }

    // ── State DTO building ────────────────────────────────────────────────────

    private static AdventureSessionStateDto BuildState(
        AdventureSession session,
        GeneratedAdventureDto adventure,
        IReadOnlyDictionary<Guid, string> participantNames)
    {
        var scenes = adventure.Scenes ?? [];
        var scene = RequireScene(adventure, session.CurrentSceneId);
        var sceneIndex = scenes.ToList().FindIndex(s => SameId(s.SceneId, scene.SceneId));

        var npcStates = AdventureContentIds.ForNpcs(adventure.Npcs)
            .Select(n => ToNpcDto(n.Id, n.Npc, session.NpcStates.FirstOrDefault(s => SameId(s.NpcId, n.Id))))
            .ToList();

        var sceneNpcs = NpcsInScene(adventure, scene)
            .Select(n => ToNpcDto(n.Id, n.Npc, session.NpcStates.FirstOrDefault(s => SameId(s.NpcId, n.Id))))
            .ToList();

        var encounterTitles = AdventureContentIds.ForEncounters(adventure.Encounters)
            .ToDictionary(e => e.Id, e => e.Encounter.Title, StringComparer.OrdinalIgnoreCase);

        return new AdventureSessionStateDto(
            Id: session.Id,
            GeneratedAdventureId: session.GeneratedAdventureId,
            CampaignId: session.CampaignId,
            SessionId: session.SessionId,
            CharacterId: session.CharacterId,
            AdventureTitle: adventure.Pitch?.Title ?? "Untitled adventure",
            Status: session.Status.ToString(),
            OutcomeNotes: session.OutcomeNotes,
            CurrentScene: new AdventureSceneDto(
                SceneId: scene.SceneId,
                Title: scene.Title,
                Description: scene.Description,
                SceneType: scene.SceneType,
                ReadAloudText: scene.ReadAloudText,
                Objectives: scene.Objectives ?? [],
                Exits: ExitsOf(scene, adventure),
                Npcs: sceneNpcs),
            CurrentSceneNumber: sceneIndex + 1,
            TotalScenes: scenes.Count,
            CompletedSceneCount: session.CompletedSceneIds.Count,
            CompletedSceneIds: session.CompletedSceneIds.ToList(),
            NpcStates: npcStates,
            RevealedClues: RevealedClues(session, adventure),
            RevealedSecrets: RevealedSecrets(session, adventure),
            EncounterOutcomes: session.EncounterOutcomes
                .Select(o => new AdventureEncounterOutcomeDto(
                    o.EncounterId,
                    encounterTitles.TryGetValue(o.EncounterId, out var t) ? t : o.EncounterId,
                    o.Outcome,
                    o.Notes))
                .ToList(),
            StartedAt: session.StartedAt,
            CompletedAt: session.CompletedAt,
            LastActivityAt: session.LastActivityAt,
            IsGroup: session.IsGroup,
            ActiveParticipantId: session.ActiveParticipantId,
            ActiveParticipantName: session.ActiveParticipantId is { } activeId ? NameOf(participantNames, activeId) : null,
            CurrentRound: session.CurrentRound,
            TurnOrder: BuildTurnOrder(session, participantNames),
            TurnAdvance: session.TurnAdvance);
    }

    private static List<ParticipantTurnDto> BuildTurnOrder(
        AdventureSession session,
        IReadOnlyDictionary<Guid, string> participantNames)
        => session.TurnOrder
            .Select(id => new ParticipantTurnDto(id, NameOf(participantNames, id), id == session.ActiveParticipantId))
            .ToList();

    private static string NameOf(IReadOnlyDictionary<Guid, string> participantNames, Guid participantId)
        => participantNames.TryGetValue(participantId, out var name) ? name : "Unknown player";

    private static AdventureNpcStateDto ToNpcDto(string id, GeneratedNpc npc, AdventureNpcState? state) => new(
        NpcId: id,
        NpcName: npc.Name,
        Role: npc.Role,
        HasBeenMet: state?.HasBeenMet ?? false,
        IsAlive: state?.IsAlive ?? true,
        RevealedInfoIds: state?.RevealedInfoIds.ToList() ?? [],
        PlayerNotes: state?.PlayerNotes);

    private static List<AdventureRevealedItemDto> RevealedClues(AdventureSession session, GeneratedAdventureDto adventure)
    {
        var known = AdventureContentIds.ForClues(adventure.Clues);
        return session.RevealedClueIds
            .Select(id =>
            {
                var match = known.FirstOrDefault(c => SameId(c.Id, id));
                return match.Id is null
                    ? new AdventureRevealedItemDto(id, id, string.Empty)
                    : new AdventureRevealedItemDto(match.Id, match.Handout.Title, match.Handout.Content);
            })
            .ToList();
    }

    private static List<AdventureRevealedItemDto> RevealedSecrets(AdventureSession session, GeneratedAdventureDto adventure)
    {
        var known = AdventureContentIds.ForSecrets(adventure.Clues);
        return session.RevealedSecretIds
            .Select(id =>
            {
                var match = known.FirstOrDefault(s => SameId(s.Id, id));
                return match.Id is null
                    ? new AdventureRevealedItemDto(id, id, string.Empty)
                    : new AdventureRevealedItemDto(match.Id, match.Secret.Title, match.Secret.Content);
            })
            .ToList();
    }

    // ── Scene helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<(string Id, GeneratedNpc Npc)> NpcsInScene(GeneratedAdventureDto adventure, GeneratedScene scene)
        => AdventureContentIds.ForNpcs(adventure.Npcs)
            .Where(n => n.Npc.SceneIds.Any(id => SameId(id, scene.SceneId)));

    private static List<AdventureExitDto> ExitsOf(GeneratedScene scene, GeneratedAdventureDto adventure)
    {
        var exits = new List<AdventureExitDto>();
        foreach (var id in scene.ConnectsTo)
        {
            var target = FindScene(adventure, id);
            if (target is not null)
                exits.Add(new AdventureExitDto(target.SceneId, target.Title, target.SceneType));
        }

        return exits;
    }

    private static GeneratedScene? FindScene(GeneratedAdventureDto adventure, string sceneId)
        => adventure.Scenes?.FirstOrDefault(s => SameId(s.SceneId, sceneId));

    private static GeneratedScene RequireScene(GeneratedAdventureDto adventure, string sceneId)
        => FindScene(adventure, sceneId)
           ?? throw new InvalidOperationException($"Scene '{sceneId}' does not exist in this adventure.");

    /// <summary>IDs come from AI output, so compare them case-insensitively.</summary>
    private static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ── Persistence helpers ───────────────────────────────────────────────────

    private async Task<AdventureSession> LoadTrackedAsync(Guid sessionId, CancellationToken ct)
        => await _db.AdventureSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
           ?? throw new KeyNotFoundException($"Adventure session '{sessionId}' not found.");

    private async Task<GeneratedAdventureDto> LoadAdventureAsync(AdventureSession session, CancellationToken ct)
    {
        var adventure = await _retrieval.GetByIdAsync(session.GeneratedAdventureId, ct)
            ?? throw new KeyNotFoundException($"Adventure '{session.GeneratedAdventureId}' not found.");

        if (adventure.Scenes is not { Count: > 0 })
            throw new InvalidOperationException("This adventure has no scenes to play.");

        return adventure;
    }

    /// <summary>The adventure must be approved and have scenes to play.</summary>
    private static void RequirePlayable(GeneratedAdventureDto adventure)
    {
        if (adventure.Status is not ("Approved" or "Active"))
        {
            throw new InvalidOperationException(
                $"Adventure is in '{adventure.Status}' status; only approved adventures can be played.");
        }

        if (adventure.Scenes is not { Count: > 0 })
            throw new InvalidOperationException("This adventure has no scenes to play.");
    }

    /// <summary>A new in-progress run at the adventure's first scene, with every NPC unmet and alive.</summary>
    private static AdventureSession NewRun(
        GeneratedAdventureDto adventure,
        Guid generatedAdventureId,
        Guid campaignId,
        Guid? characterId)
    {
        var now = DateTimeOffset.UtcNow;
        return new AdventureSession
        {
            Id = Guid.NewGuid(),
            GeneratedAdventureId = generatedAdventureId,
            CampaignId = campaignId,
            CharacterId = characterId,
            CurrentSceneId = adventure.Scenes![0].SceneId,
            Status = AdventureSessionStatus.InProgress,
            StartedAt = now,
            LastActivityAt = now,
            NpcStates = AdventureContentIds.ForNpcs(adventure.Npcs)
                .Select(n => new AdventureNpcState
                {
                    NpcId = n.Id,
                    NpcName = n.Npc.Name,
                    HasBeenMet = false,
                    IsAlive = true,
                })
                .ToList(),
        };
    }

    private static void RequireGroup(AdventureSession session)
    {
        if (!session.IsGroup || session.TurnOrder.Count == 0)
            throw new InvalidOperationException("This adventure run is not a group run.");
    }

    /// <summary>
    /// Display names of the run's participants, by ID. Empty for a solo run.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadParticipantNamesAsync(
        AdventureSession session, CancellationToken ct)
    {
        if (session.TurnOrder.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = session.TurnOrder;
        return await _db.SessionParticipants
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
    }

    /// <summary>
    /// Character name by participant ID for the run's participants who have a character assigned,
    /// so the AI can address each player in character. Participants without one are left out.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadCharacterNamesAsync(
        AdventureSession session, CancellationToken ct)
    {
        if (session.TurnOrder.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = session.TurnOrder;
        var assigned = await _db.SessionParticipants
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.CharacterId != null)
            .Select(p => new { p.Id, CharacterId = p.CharacterId!.Value })
            .ToListAsync(ct);
        if (assigned.Count == 0)
            return new Dictionary<Guid, string>();

        var characterIds = assigned.Select(a => a.CharacterId).ToList();
        var names = await _db.Characters
            .AsNoTracking()
            .Where(c => characterIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return assigned
            .Where(a => names.TryGetValue(a.CharacterId, out var name) && !string.IsNullOrWhiteSpace(name))
            .ToDictionary(a => a.Id, a => names[a.CharacterId]);
    }

    /// <summary>
    /// Of <paramref name="participantIds"/>, those who are approved players (or the host) of the
    /// play session, with their display names. Spectators and a human DM do not take turns.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> FindActiveParticipantsAsync(
        Guid playSessionId, IReadOnlyList<Guid> participantIds, CancellationToken ct)
        => await _db.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == playSessionId
                        && p.IsApproved
                        && (p.Role == ParticipantRole.Player || p.Role == ParticipantRole.Host)
                        && participantIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

    private static void RequireInProgress(AdventureSession session)
    {
        if (session.Status != AdventureSessionStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"Adventure session is {session.Status}; it can no longer be changed.");
        }
    }

    private static void Touch(AdventureSession session) => session.LastActivityAt = DateTimeOffset.UtcNow;

    // ── Audit + broadcast (best effort) ───────────────────────────────────────

    /// <summary>
    /// Appends a non-reversible audit event to the campaign log. Not a "Command:" event, so undo
    /// and replay leave it alone. Failure is logged, never thrown: the state change is already saved.
    /// </summary>
    private async Task AppendEventAsync(AdventureSession session, string eventType, object payload, CancellationToken ct)
    {
        try
        {
            await _campaignState.AppendEventAsync(
                new AppendEventRequest(
                    CampaignId: session.CampaignId,
                    EventType: eventType,
                    PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
                    ActorType: ActorType,
                    Reversible: false,
                    SessionId: session.SessionId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to append {EventType} event for adventure session {AdventureSessionId}", eventType, session.Id);
        }
    }

    private Task NotifyAsync(AdventureSession session, Func<ISessionHubNotifier, Task> send)
        => NotifyAsync(session.SessionId, session.Id, send);

    private async Task NotifyAsync(Guid? playSessionId, Guid runId, Func<ISessionHubNotifier, Task> send)
    {
        if (_hubNotifier is null || playSessionId is null)
            return;

        try
        {
            await send(_hubNotifier);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast adventure event for adventure session {AdventureSessionId}", runId);
        }
    }

    /// <summary>
    /// Tells the play session's group that something players can see changed. A signal only: it carries the
    /// run id and a reason, never scene text, narration or notes, so it cannot leak hidden content. Clients
    /// respond by reading the player view.
    /// </summary>
    private Task NotifyStateChangedAsync(Guid? playSessionId, Guid runId, string reason, CancellationToken ct)
        => NotifyAsync(playSessionId, runId, hub => hub.NotifyAdventureStateChangedAsync(
            playSessionId!.Value,
            new AdventureStateChangedNotification(runId, reason, DateTimeOffset.UtcNow),
            ct));
}
