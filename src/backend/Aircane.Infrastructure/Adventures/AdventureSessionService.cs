using System.Text;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.Adventures;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
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

    public AdventureSessionService(
        AircaneDbContext db,
        IAdventureRetrievalService retrieval,
        ICampaignStateService campaignState,
        ILogger<AdventureSessionService> logger,
        ISessionHubNotifier? hubNotifier = null)
    {
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
            if (existing.GeneratedAdventureId == generatedAdventureId)
                return existing;

            throw new InvalidOperationException(
                "This campaign already has an adventure in progress. Abandon or complete it before starting another.");
        }

        if (adventure.Status is not ("Approved" or "Active"))
        {
            throw new InvalidOperationException(
                $"Adventure is in '{adventure.Status}' status; only approved adventures can be played.");
        }

        var scenes = adventure.Scenes;
        if (scenes is null || scenes.Count == 0)
            throw new InvalidOperationException("This adventure has no scenes to play.");

        if (characterId is not null && !await _db.Characters.AnyAsync(c => c.Id == characterId, ct))
            throw new KeyNotFoundException($"Character '{characterId}' not found.");

        var now = DateTimeOffset.UtcNow;
        var session = new AdventureSession
        {
            Id = Guid.NewGuid(),
            GeneratedAdventureId = generatedAdventureId,
            CampaignId = campaignId,
            CharacterId = characterId,
            CurrentSceneId = scenes[0].SceneId,
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

        _db.AdventureSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Started adventure session {AdventureSessionId} for adventure {AdventureId} in campaign {CampaignId}",
            session.Id, generatedAdventureId, campaignId);

        await AppendEventAsync(session, "Adventure:Started",
            new { adventureSessionId = session.Id, adventureId = generatedAdventureId, sceneId = session.CurrentSceneId }, ct);

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

        return BuildState(session, adventure);
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

        return new AdventureContext(session.Id, BuildAdventureContext(session, adventure));
    }

    /// <inheritdoc />
    public string BuildAdventureContext(AdventureSession session, GeneratedAdventureDto adventure)
    {
        var scenes = adventure.Scenes ?? [];
        var scene = FindScene(adventure, session.CurrentSceneId);
        var title = adventure.Pitch?.Title ?? "Untitled adventure";

        var sb = new StringBuilder();
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

    private static AdventureSessionStateDto BuildState(AdventureSession session, GeneratedAdventureDto adventure)
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
            LastActivityAt: session.LastActivityAt);
    }

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

    private async Task NotifyAsync(AdventureSession session, Func<ISessionHubNotifier, Task> send)
    {
        if (_hubNotifier is null || session.SessionId is null)
            return;

        try
        {
            await send(_hubNotifier);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast adventure event for adventure session {AdventureSessionId}", session.Id);
        }
    }
}
