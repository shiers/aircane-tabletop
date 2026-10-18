using System.Text;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.GameSystems;
using Aircane.Domain.Combat;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Orchestrates the full AI DM loop for a player action:
/// 1. Load campaign state (current scene + character state)
/// 2. Build RAG context (rules + adventure)
/// 3. Build AI prompt with state, context, and player action
/// 4. Call AI provider for structured output
/// 5. Parse structured output
/// 6. Execute each proposed action via StateCommandExecutor
/// 7. Return narration and action results
/// </summary>
public sealed class PlayerActionService : IPlayerActionService
{
    private readonly ICampaignService _campaignService;
    private readonly ICampaignStateService _campaignStateService;
    private readonly IRagContextBuilder _ragContextBuilder;
    private readonly IAiProvider _aiProvider;
    private readonly AiOutputParser _outputParser;
    private readonly IStateCommandExecutor _stateCommandExecutor;
    private readonly ISystemRegistry _systemRegistry;
    private readonly IAiContextAdapter _aiContextAdapter;
    private readonly AircaneDbContext _db;
    private readonly ILogger<PlayerActionService> _logger;
    private readonly IAdventureSessionService? _adventureSessions;
    private readonly IAiRoleConfigurationService? _roleService;
    private readonly IAiAuthorityService? _authorityService;

    /// <summary>
    /// The solo-adventure collaborators are optional trailing parameters so the service still works
    /// (without adventure support) where they are not registered. When <paramref name="adventureSessionService"/>
    /// is supplied but a role/authority service is not, adventure actions fail closed.
    /// </summary>
    public PlayerActionService(
        ICampaignService campaignService,
        ICampaignStateService campaignStateService,
        IRagContextBuilder ragContextBuilder,
        IAiProvider aiProvider,
        AiOutputParser outputParser,
        IStateCommandExecutor stateCommandExecutor,
        ISystemRegistry systemRegistry,
        IAiContextAdapter aiContextAdapter,
        AircaneDbContext db,
        ILogger<PlayerActionService> logger,
        IAdventureSessionService? adventureSessionService = null,
        IAiRoleConfigurationService? roleService = null,
        IAiAuthorityService? authorityService = null)
    {
        _adventureSessions = adventureSessionService;
        _roleService = roleService;
        _authorityService = authorityService;
        _campaignService = campaignService;
        _campaignStateService = campaignStateService;
        _ragContextBuilder = ragContextBuilder;
        _aiProvider = aiProvider;
        _outputParser = outputParser;
        _stateCommandExecutor = stateCommandExecutor;
        _systemRegistry = systemRegistry;
        _aiContextAdapter = aiContextAdapter;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlayerActionResponse> ProcessActionAsync(
        PlayerActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionText);

        _logger.LogDebug(
            "Processing player action for session {SessionId}, character {CharacterId}: {ActionText}",
            request.SessionId, request.CharacterId, request.ActionText);

        // 1. Load campaign configuration
        var campaign = await _campaignService.GetCampaignAsync(request.CampaignId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign {request.CampaignId} not found.");

        // 2. Load current campaign state (scene, party, world flags)
        var state = await _campaignStateService.LoadStateAsync(request.CampaignId, cancellationToken);

        // 3. If the campaign is running a solo adventure, load its context (current scene, objectives,
        // exits, NPCs, what the player already knows, any pending roll). It is injected straight into
        // the prompt, which also decides how retrieval runs below.
        var adventureContext = await BuildAdventureContextAsync(request.CampaignId, cancellationToken);
        var inAdventure = adventureContext is not null;

        // 3a. Build RAG context (rules + adventure context)
        var ragRequest = new RagContextRequest(
            Query: request.ActionText,
            GameSystem: campaign.GameSystem,
            Ruleset: campaign.Ruleset,
            RequesterRole: ParticipantRole.Host, // AI DM has full visibility
            TopK: 10,
            MaxContextChars: 8000,
            MinRelevanceScore: 0.4,
            // With reranking active: retrieve 25 candidates, keep the best 7 after reranking.
            // During an adventure run reranking is skipped: the adventure context is already in the
            // prompt, so the extra seconds of rerank latency buy little in interactive play. The
            // standard pool and the standard score floor apply instead.
            RerankCandidateTopK: inAdventure ? 10 : 25,
            PostRerankTopN: inAdventure ? null : 7,
            SkipReranking: inAdventure);

        var ragResult = await _ragContextBuilder.BuildContextAsync(ragRequest, cancellationToken);

        // 3b. Build the live combat context block, if an encounter is active. This lets the AI DM
        // reference HP, conditions, and whose turn it is when narrating.
        var combatContext = await BuildCombatContextAsync(request.CampaignId, state, cancellationToken);

        // 4. Build AI prompt with state + adventure + combat + context + player action
        var messages = BuildPromptMessages(
            request, campaign, state, ragResult, combatContext, adventureContext?.Text,
            adventureContext?.IsGroup ?? false, adventureContext?.PlayersWithoutCharacter);

        // 5. Call AI provider for structured output
        var aiOutput = await _aiProvider.StructuredChatCompletionAsync(messages, cancellationToken);

        _logger.LogDebug(
            "AI returned narration ({NarrationLength} chars) with {ActionCount} proposed actions",
            aiOutput.Narration.Length, aiOutput.ProposedActions.Count);

        // 6. Execute each proposed action via StateCommandExecutor
        var commandContext = new StateCommandContext(
            CampaignId: request.CampaignId,
            SessionId: request.SessionId,
            AiRole: campaign.AiRole,
            AiAuthority: campaign.AiAuthority);

        var actionResults = new List<ProposedActionResult>();
        foreach (var action in aiOutput.ProposedActions)
        {
            // Adventure actions mutate the adventure run, not the campaign state snapshot, so they
            // are applied through IAdventureSessionService rather than the state command executor.
            var result = IsAdventureAction(action.Type)
                ? await ApplyAdventureActionAsync(action, campaign, adventureContext?.SessionId, cancellationToken)
                : await _stateCommandExecutor.ExecuteAsync(action, commandContext, cancellationToken);
            actionResults.Add(new ProposedActionResult(
                ActionType: action.Type,
                Label: action.Label,
                Outcome: result.Outcome,
                ErrorMessage: result.ErrorMessage,
                ProposalId: result.ProposalId));
        }

        // 6b. Remember a roll the AI asked for so the player's next message ("I rolled a 14") is read
        // as its result. Every action consumes the previous pending roll, so this also clears it.
        await UpdatePendingRollAsync(adventureContext, aiOutput.ProposedActions, cancellationToken);

        // 6c. Save the exchange to the run's transcript so the player can resume later.
        await RecordExchangeAsync(adventureContext, request.ActionText, aiOutput.Narration, cancellationToken);

        // 7. Map citations, enriching built-in sources with license metadata (Phase 10.6).
        var licenseByDocId = await CitationLicenseEnricher.BuildLicenseMapAsync(
            _db, ragResult.Citations.Select(c => c.SourceDocumentId), cancellationToken);

        var citations = ragResult.Citations
            .Select(c =>
            {
                licenseByDocId.TryGetValue(c.SourceDocumentId, out var lic);
                return new PlayerActionCitation(
                    SourceTitle: c.SourceDocumentTitle,
                    PageNumber: c.PageNumber,
                    SectionTitle: c.SectionTitle,
                    ChunkId: c.ChunkId,
                    LicenseKey: lic.LicenseKey,
                    LicenseDisplayName: lic.LicenseDisplayName,
                    AttributionText: lic.AttributionText);
            })
            .ToList();

        _logger.LogInformation(
            "Player action processed for session {SessionId}: {AppliedCount} applied, {QueuedCount} queued, {FailedCount} failed",
            request.SessionId,
            actionResults.Count(r => r.Outcome == StateCommandOutcome.Applied),
            actionResults.Count(r => r.Outcome == StateCommandOutcome.QueuedForApproval),
            actionResults.Count(r => r.Outcome is StateCommandOutcome.ValidationFailed or StateCommandOutcome.PermissionDenied));

        return new PlayerActionResponse(
            Narration: aiOutput.Narration,
            PrivateDmNote: aiOutput.PrivateDmNote,
            ProposedActions: actionResults,
            Citations: citations);
    }

    /// <summary>
    /// Deserializes the live <see cref="EncounterState"/> from the campaign state (the
    /// <c>encounter</c> key) and renders a compact combat context block via
    /// <see cref="IAiContextAdapter.BuildCombatContext"/>. Returns an empty string when there is
    /// no active encounter. Failures are non-fatal — combat context is best-effort and must never
    /// break the AI DM loop.
    /// </summary>
    private async Task<string?> BuildCombatContextAsync(
        Guid campaignId,
        Application.DTOs.CampaignState.CampaignStateDto state,
        CancellationToken cancellationToken)
    {
        var encounter = TryReadEncounter(state.StateJson);
        if (encounter is null || !encounter.IsActive)
            return null;

        // Resolve the bound game-system definition to decide whether to include action slots
        // (hidden for freeform). Missing/unbound definitions are tolerated: we still render the
        // combat summary, just without action-economy detail.
        Domain.Entities.GameSystems.GameSystemDefinition? definition = null;
        try
        {
            definition = await _systemRegistry.GetByCampaignAsync(campaignId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "No bound game-system definition for campaign {CampaignId}; combat context will omit action slots.",
                campaignId);
        }

        return _aiContextAdapter.BuildCombatContext(encounter, definition);
    }

    /// <summary>
    /// Loads the in-progress adventure run's prompt context, or null when the campaign is not
    /// running an adventure. Non-fatal: adventure context is best-effort and must never break the
    /// AI DM loop (the AI simply plays without it).
    /// </summary>
    private async Task<AdventureContext?> BuildAdventureContextAsync(
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        if (_adventureSessions is null)
            return null;

        try
        {
            return await _adventureSessions.BuildActiveAdventureContextAsync(campaignId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not load adventure context for campaign {CampaignId}; continuing without it.", campaignId);
            return null;
        }
    }

    /// <summary>
    /// Saves the player's action and the AI's narration to the run's transcript. Best effort: the
    /// narration history is a convenience and must never block or fail gameplay.
    /// </summary>
    private async Task RecordExchangeAsync(
        AdventureContext? adventureContext,
        string playerAction,
        string narration,
        CancellationToken cancellationToken)
    {
        if (_adventureSessions is null || adventureContext is null)
            return;

        try
        {
            await _adventureSessions.RecordExchangeAsync(
                adventureContext.SessionId, playerAction, narration, adventureContext.CurrentSceneId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not save the narration history for adventure session {AdventureSessionId}", adventureContext.SessionId);
        }
    }

    /// <summary>
    /// Sets the run's pending roll from the last labelled RequestRoll in this response, or clears it
    /// when there was none. Best effort: failing to record it must not fail the player's turn.
    /// </summary>
    private async Task UpdatePendingRollAsync(
        AdventureContext? adventureContext,
        IReadOnlyList<Application.Abstractions.AiProposedAction> actions,
        CancellationToken cancellationToken)
    {
        if (_adventureSessions is null || adventureContext is null)
            return;

        var roll = actions.LastOrDefault(a =>
            a.Type == AiActionType.RequestRoll && !string.IsNullOrWhiteSpace(a.Label));

        try
        {
            await _adventureSessions.UpdatePendingRollAsync(
                adventureContext.SessionId, roll?.Label, roll?.Dc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not update the pending roll for adventure session {AdventureSessionId}", adventureContext.SessionId);
        }
    }

    /// <summary>True for the action types that operate on the active adventure run.</summary>
    internal static bool IsAdventureAction(AiActionType type) => type is
        AiActionType.AdvanceScene or
        AiActionType.RevealClue or
        AiActionType.RevealSecret or
        AiActionType.UpdateNpc or
        AiActionType.CompleteAdventure;

    /// <summary>
    /// Applies an AI-proposed adventure action to the campaign's active adventure run.
    /// <para>
    /// Role and authority are enforced as for any other AI action, but because the proposal queue
    /// cannot replay adventure actions, an action the campaign's authority would queue for approval
    /// is refused instead (<see cref="StateCommandOutcome.PermissionDenied"/>) with a message
    /// saying why. Adventure rules (a reachable scene, a known clue ID, a run still in progress)
    /// are validated by <see cref="IAdventureSessionService"/>; a violation is reported as
    /// <see cref="StateCommandOutcome.ValidationFailed"/> and never aborts the rest of the response.
    /// </para>
    /// </summary>
    private async Task<StateCommandResult> ApplyAdventureActionAsync(
        Application.Abstractions.AiProposedAction action,
        Application.DTOs.Campaigns.CampaignDto campaign,
        Guid? adventureSessionId,
        CancellationToken cancellationToken)
    {
        if (_adventureSessions is null || adventureSessionId is null)
            return StateCommandResult.ValidationFailed("No adventure is in progress for this campaign.");

        if (_roleService is null || _authorityService is null)
            return StateCommandResult.PermissionDenied("Adventure actions are unavailable: AI role/authority services are not configured.");

        if (!_roleService.IsActionPermitted(campaign.AiRole, action.Type))
        {
            return StateCommandResult.PermissionDenied(
                $"AI role '{campaign.AiRole}' does not have permission for action '{action.Type}'.");
        }

        if (!_authorityService.CanAutoApply(campaign.AiAuthority, action.Type))
        {
            return StateCommandResult.PermissionDenied(
                $"AI authority '{campaign.AiAuthority}' does not allow '{action.Type}' to be applied automatically. " +
                "Set the campaign's AI authority to Full Session Control for hands-off solo play, or advance manually.");
        }

        var id = adventureSessionId.Value;
        try
        {
            switch (action.Type)
            {
                case AiActionType.AdvanceScene:
                    if (string.IsNullOrWhiteSpace(action.SceneId))
                        return StateCommandResult.ValidationFailed("AdvanceScene requires a sceneId.");
                    await _adventureSessions.AdvanceSceneAsync(id, action.SceneId, action.Narration, cancellationToken);
                    break;

                case AiActionType.RevealClue:
                    if (string.IsNullOrWhiteSpace(action.ClueId))
                        return StateCommandResult.ValidationFailed("RevealClue requires a clueId.");
                    await _adventureSessions.RevealClueAsync(id, action.ClueId, cancellationToken);
                    break;

                case AiActionType.RevealSecret:
                    if (string.IsNullOrWhiteSpace(action.SecretId))
                        return StateCommandResult.ValidationFailed("RevealSecret requires a secretId.");
                    await _adventureSessions.RevealSecretAsync(id, action.SecretId, cancellationToken);
                    break;

                case AiActionType.UpdateNpc:
                    if (string.IsNullOrWhiteSpace(action.NpcId))
                        return StateCommandResult.ValidationFailed("UpdateNpc requires an npcId.");
                    await _adventureSessions.UpdateNpcStateAsync(
                        id, action.NpcId, action.HasBeenMet, action.IsAlive,
                        action.RevealedInfoId, action.Notes, cancellationToken);
                    break;

                case AiActionType.CompleteAdventure:
                    if (string.IsNullOrWhiteSpace(action.OutcomeNotes))
                        return StateCommandResult.ValidationFailed("CompleteAdventure requires outcomeNotes.");
                    await _adventureSessions.CompleteAdventureAsync(id, action.OutcomeNotes, cancellationToken);
                    break;
            }

            return StateCommandResult.Applied();
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            _logger.LogWarning(ex,
                "Adventure action {ActionType} rejected for adventure session {AdventureSessionId}: {Reason}",
                action.Type, id, ex.Message);
            return StateCommandResult.ValidationFailed(ex.Message);
        }
    }

    /// <summary>
    /// Reads the <c>encounter</c> key from the serialized campaign state and deserializes it into
    /// an <see cref="EncounterState"/>. Returns null when absent, null-valued, or malformed.
    /// </summary>
    internal static EncounterState? TryReadEncounter(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(stateJson);
            if (!doc.RootElement.TryGetProperty("encounter", out var encEl) ||
                encEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return encEl.Deserialize<EncounterState>(
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the prompt messages for the AI DM, including system instructions,
    /// campaign state, RAG context, and the player's action.
    /// </summary>
    /// <param name="isGroupRun">
    /// True when <paramref name="adventureContext"/> belongs to a group run: the group instruction
    /// block is sent instead of the solo one. Ignored when there is no adventure context.
    /// </param>
    /// <param name="playersWithoutCharacter">
    /// Group runs only: the participants with no character in this app. The group no-character block
    /// names just these players, and is left out when everyone has a character.
    /// </param>
    public static IReadOnlyList<AiMessage> BuildPromptMessages(
        PlayerActionRequest request,
        Application.DTOs.Campaigns.CampaignDto campaign,
        Application.DTOs.CampaignState.CampaignStateDto state,
        RagContextResult ragResult,
        string? combatContext = null,
        string? adventureContext = null,
        bool isGroupRun = false,
        IReadOnlyList<string>? playersWithoutCharacter = null)
    {
        var groupRun = isGroupRun && !string.IsNullOrWhiteSpace(adventureContext);
        var messages = new List<AiMessage>();

        // System message with AI DM instructions
        messages.Add(AiMessage.System(BuildSystemPrompt(campaign)));

        // Campaign state context
        var stateContext = BuildStateContext(state);
        if (!string.IsNullOrWhiteSpace(stateContext))
        {
            messages.Add(AiMessage.System(stateContext));
        }

        // No character attached: the player runs their character elsewhere, so do not use stats.
        // A group run has no run-level character, so it names only the players who lack one, if any.
        if (groupRun)
        {
            if (playersWithoutCharacter is { Count: > 0 })
                messages.Add(AiMessage.System(BuildGroupNoCharacterInstructions(playersWithoutCharacter)));
        }
        else if (request.CharacterId == Guid.Empty)
        {
            messages.Add(AiMessage.System(BuildNoCharacterInstructions()));
        }

        // Adventure run: how to drive the adventure (action schema) + where the players are in it.
        // Exactly one instruction block, chosen by run type: the solo and group blocks are never layered.
        if (!string.IsNullOrWhiteSpace(adventureContext))
        {
            messages.Add(AiMessage.System(groupRun ? BuildGroupAdventureInstructions() : BuildAdventureInstructions()));
            messages.Add(AiMessage.System($"ACTIVE ADVENTURE:\n{adventureContext}"));
        }

        // Live combat context (initiative, HP, conditions, whose turn it is)
        if (!string.IsNullOrWhiteSpace(combatContext))
        {
            messages.Add(AiMessage.System(combatContext));
        }

        // RAG context (rules + adventure)
        if (ragResult.ChunksIncluded > 0 && !string.IsNullOrWhiteSpace(ragResult.ContextText))
        {
            messages.Add(AiMessage.System($"RETRIEVED RULES AND ADVENTURE CONTEXT:\n{ragResult.ContextText}"));
        }

        // Player action as user message. Solo adventure play may have no formal character.
        var userMessage = request.CharacterId == Guid.Empty
            ? $"Player action: {request.ActionText}"
            : $"[Character {request.CharacterId}] Player action: {request.ActionText}";
        messages.Add(AiMessage.User(userMessage));

        return messages;
    }

    /// <summary>
    /// Added when the session has no linked character. The player keeps their character in another
    /// tool, so the AI must ask for rolls in words and wait for the player to report the result.
    /// </summary>
    public static string BuildNoCharacterInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("NO CHARACTER ATTACHED:");
        sb.AppendLine("The player has no character in this app; they play it with their own tools and report results to you.");
        sb.AppendLine("- When a roll is needed, say exactly what to roll (e.g. \"Roll Dexterity (Stealth)\") and the DC if there is one.");
        sb.AppendLine("- You may include a RequestRoll proposed action (use characterId null) so the request is recorded.");
        sb.AppendLine("- Do NOT apply modifiers, quote the character's stats, or assume proficiencies, HP or equipment.");
        sb.AppendLine("- Stop after asking for the roll. Do not narrate its outcome: wait for the player's next message (e.g. \"I rolled a 14\") and then resolve it.");
        return sb.ToString();
    }

    /// <summary>
    /// The group counterpart of <see cref="BuildNoCharacterInstructions"/>, naming only the
    /// participants who have no character in this app. Not sent when every player has one.
    /// </summary>
    public static string BuildGroupNoCharacterInstructions(IReadOnlyList<string> playersWithoutCharacter)
    {
        ArgumentNullException.ThrowIfNull(playersWithoutCharacter);

        var sb = new StringBuilder();
        sb.AppendLine("NO CHARACTER ATTACHED (SOME PLAYERS):");
        sb.AppendLine($"These players have no character in this app; they play with their own tools and report results to you: {string.Join(", ", playersWithoutCharacter)}.");
        sb.AppendLine($"- When one of them needs to roll, name them and say exactly what to roll (e.g. \"{playersWithoutCharacter[0]}, roll Dexterity (Stealth)\") and the DC if there is one.");
        sb.AppendLine("- You may include a RequestRoll proposed action (use characterId null) so the request is recorded.");
        sb.AppendLine("- For these players, do NOT apply modifiers, quote stats, or assume proficiencies, HP or equipment.");
        sb.AppendLine("- Stop after asking for the roll. Do not narrate its outcome: wait for the result to be reported and then resolve it.");
        return sb.ToString();
    }

    /// <summary>
    /// Instructions for running a solo adventure: what the AI DM must do and the exact
    /// <c>proposedActions</c> shapes that move the adventure forward. IDs in the examples are the
    /// ones printed in square brackets in the adventure context. Group runs get
    /// <see cref="BuildGroupAdventureInstructions"/> instead.
    /// </summary>
    public static string BuildAdventureInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SOLO ADVENTURE MODE:");
        sb.AppendLine("You are running a single player through the ACTIVE ADVENTURE below, scene by scene.");
        sb.AppendLine("- Narrate the outcome of the player's action and steer toward the current scene's objectives.");
        sb.AppendLine("- Never reveal DM-only text (DM notes, hidden secrets) until the player has earned it.");
        sb.AppendLine("- Use the IDs in square brackets from the adventure context in proposedActions. Use these action shapes:");
        sb.AppendLine("  - {\"type\":\"revealClue\",\"clueId\":\"clue-1\"} when the player finds a listed clue.");
        sb.AppendLine("  - {\"type\":\"revealSecret\",\"secretId\":\"secret-1\"} when the player uncovers a listed secret.");
        sb.AppendLine("  - {\"type\":\"updateNpc\",\"npcId\":\"npc-example\",\"hasBeenMet\":true,\"isAlive\":false,\"revealedInfoId\":\"short-label\",\"notes\":\"what the player learned\"} - include only the fields that changed.");
        sb.AppendLine("  - {\"type\":\"advanceScene\",\"sceneId\":\"scene-2\",\"narration\":\"transition narration\"} - only after the scene's objectives are met (or the fail-forward rule applies) AND the player has chosen one of the Available Exits. sceneId must be one of the Available Exits.");
        sb.AppendLine("  - {\"type\":\"completeAdventure\",\"outcomeNotes\":\"how the adventure ended\"} - when the final scene's objectives are met and the story is resolved.");
        sb.AppendLine("- Do not advance the scene or complete the adventure early; wait for the player to act.");
        sb.AppendLine();
        sb.AppendLine(BuildSkillCheckRules());
        sb.AppendLine();
        sb.AppendLine(BuildExitRules());
        sb.AppendLine();
        sb.AppendLine(BuildEndOfNarrationRule());
        return sb.ToString();
    }

    /// <summary>
    /// Instructions for running a group adventure: the counterpart of
    /// <see cref="BuildAdventureInstructions"/> for a party of players taking turns, sent instead of
    /// it, never alongside. Covers turn rotation, addressing the acting player in character, input
    /// from players whose turn it is not, and events that affect the whole party. Names and turn
    /// order come from the GROUP PLAY section of the adventure context.
    /// </summary>
    public static string BuildGroupAdventureInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("GROUP ADVENTURE MODE:");
        sb.AppendLine("You are running a party of several players through the ACTIVE ADVENTURE below, scene by scene. The GROUP PLAY section of the adventure context lists the turn order, who is currently acting, and the round.");
        sb.AppendLine("- Every message you receive is sent by the currently acting player. Narrate the outcome of their character's action and steer the party toward the current scene's objectives.");
        sb.AppendLine("- The whole table reads your narration: write it for everyone present, while resolving only the acting character's action.");
        sb.AppendLine("- Never reveal DM-only text (DM notes, hidden secrets) until the party has earned it.");
        sb.AppendLine("- Use the IDs in square brackets from the adventure context in proposedActions. Use these action shapes:");
        sb.AppendLine("  - {\"type\":\"revealClue\",\"clueId\":\"clue-1\"} when a character finds a listed clue.");
        sb.AppendLine("  - {\"type\":\"revealSecret\",\"secretId\":\"secret-1\"} when a character uncovers a listed secret.");
        sb.AppendLine("  - {\"type\":\"updateNpc\",\"npcId\":\"npc-example\",\"hasBeenMet\":true,\"isAlive\":false,\"revealedInfoId\":\"short-label\",\"notes\":\"what the party learned\"} - include only the fields that changed.");
        sb.AppendLine("  - {\"type\":\"advanceScene\",\"sceneId\":\"scene-2\",\"narration\":\"transition narration\"} - only after the scene's objectives are met (or the fail-forward rule applies) AND the acting player has chosen one of the Available Exits for the party. sceneId must be one of the Available Exits.");
        sb.AppendLine("  - {\"type\":\"completeAdventure\",\"outcomeNotes\":\"how the adventure ended\"} - when the final scene's objectives are met and the story is resolved.");
        sb.AppendLine("- Do not advance the scene or complete the adventure early; wait for the party to act.");
        sb.AppendLine();
        sb.AppendLine("TURN ROTATION - players act one at a time, in the turn order listed under GROUP PLAY:");
        sb.AppendLine("- Only the character listed as Currently acting takes a turn. The host passes the turn to the next player; you never do. Do not announce that the turn has passed or narrate as if someone else is now acting.");
        sb.AppendLine("- Keep the spotlight on the acting character. Other characters may react in small ways (a glance, a step back, a hand on a weapon), but never choose their actions, words or decisions for them.");
        sb.AppendLine("- Over a round, give every character something to react to. When it fits the scene, leave an opening for the next character in the turn order (a sound only they would notice, an NPC turning toward them) without asking them to act yet.");
        sb.AppendLine("- Address the acting player by their character's name: the name listed for them under GROUP PLAY, before \"played by\" (which gives the player's own name). When no character is listed for a player, use the name shown.");
        sb.AppendLine("- If an ACTIVE COMBAT ENCOUNTER section is present, its initiative order and Active turn decide who acts in combat. Narrate attacks and other combat actions only for the combatant whose turn it is.");
        sb.AppendLine();
        sb.AppendLine("OUT-OF-TURN INPUT - a message may also describe what other characters do or say (another player calling out across the table, or the acting player speaking for the group):");
        sb.AppendLine("- Resolve only the acting character's action. Do not request rolls, apply effects, or reveal clues or secrets for anyone else's out-of-turn action.");
        sb.AppendLine("- Acknowledge the rest in a sentence and say it waits for that character's turn, e.g. \"Thorn reaches for the axe - that can wait for Thorn's turn.\"");
        sb.AppendLine("- Instant reactions that need no roll and change nothing (shouting a warning, stepping aside) can be narrated as they happen.");
        sb.AppendLine("- Never let an out-of-turn action replace or cancel the acting character's turn.");
        sb.AppendLine();
        sb.AppendLine("PARTY-WIDE EVENTS - when something affects everyone present (a trap, an ambush, a collapsing floor, arriving in a new scene):");
        sb.AppendLine("- Narrate it for the whole party, naming each character it affects and how it lands on each one.");
        sb.AppendLine("- When several characters must roll for the same event, emit ONE RequestRoll whose label names the check and every character who rolls, e.g. \"Dexterity saving throw - Mira, Thorn, Cara\", and ask for each character's result. When the results come in, resolve each character's outcome separately.");
        sb.AppendLine("- State damage, healing and conditions for each affected character in the narration (e.g. \"Mira takes 7 piercing damage\"). Only emit ApplyDamage, ApplyHealing, ApplyCondition or RemoveCondition with a characterId you were given; never invent one.");
        sb.AppendLine("- Read-aloud text and exit descriptions are for the whole party. The acting player chooses which exit the party takes.");
        sb.AppendLine();
        sb.AppendLine(BuildSkillCheckRules());
        sb.AppendLine();
        sb.AppendLine(BuildExitRules());
        sb.AppendLine();
        sb.AppendLine(BuildGroupEndOfNarrationRule());
        return sb.ToString();
    }

    /// <summary>
    /// The group counterpart of <see cref="BuildEndOfNarrationRule"/>: every narration closes by
    /// handing the moment back to the acting character, by name, so the table knows whose move it is.
    /// </summary>
    public static string BuildGroupEndOfNarrationRule()
    {
        var sb = new StringBuilder();
        sb.AppendLine("END OF NARRATION - always close your narration by handing the moment back to the acting character, by their character's name. This must be one of:");
        sb.AppendLine("- A direct question: \"Mira, what do you do?\", \"How does Thorn respond?\"");
        sb.AppendLine("- A presented choice: \"Mira, do you press forward or hold your ground?\"");
        sb.AppendLine("- A clear tension statement: \"Every eye in the hall turns to Mira.\", \"The moment hangs in the air, waiting on Thorn.\"");
        sb.AppendLine("Never end narration with description alone, and never close by addressing a character whose turn it is not. The acting player must always know it is still their move.");
        return sb.ToString();
    }

    /// <summary>
    /// Every narration closes by handing the turn back, so the player never wonders whether the AI is
    /// finished or whether they are expected to act.
    /// </summary>
    public static string BuildEndOfNarrationRule()
    {
        var sb = new StringBuilder();
        sb.AppendLine("END OF NARRATION - always close your narration with a clear signal that it is the player's turn to act. This must be one of:");
        sb.AppendLine("- A direct question: \"What do you do?\", \"How do you respond?\", \"What is your next move?\"");
        sb.AppendLine("- A presented choice: \"Do you press forward or hold your ground?\", \"Do you speak first, or wait for them to address you?\"");
        sb.AppendLine("- A clear tension statement: \"Both figures wait in silence for your response.\", \"The moment hangs in the air.\"");
        sb.AppendLine("Never end narration with description alone. The player must always know it is their turn.");
        return sb.ToString();
    }

    /// <summary>
    /// Exits are more than navigation links: when the player arrives somewhere, or asks where they can
    /// go, the AI describes each way onward so the player can make a meaningful choice.
    /// </summary>
    public static string BuildExitRules()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SCENE EXITS - whenever a new scene begins, whenever the player asks what they can see or where they can go, or whenever a choice between exits is relevant, describe EVERY available exit with at least one sensory detail (sight, sound, smell, or feeling). The player must be able to picture what lies ahead before choosing. Exit descriptions must appear in the narration, not just as names.");
        sb.AppendLine("WRONG: \"Two paths are available: Scavengers at the Threshold and The Chamber of the Unrisen Sun.\"");
        sb.AppendLine("CORRECT: \"Two paths lead onward. To your left, a low archway opens onto a chamber where pale blue light filters through cracked stone - the air carries the faint smell of old incense and cold metal. To your right, a wider passage echoes with the sound of something scraping rhythmically against the floor in the dark.\"");
        sb.AppendLine("If the scene has only one exit, describe it. If the scene has no exits, tell the player what must be done to progress.");
        return sb.ToString();
    }

    /// <summary>
    /// Rules that stop the AI DM narrating auto-successes: risky actions must be rolled for, the
    /// attempt is narrated first, and the outcome waits for the player's reported result.
    /// </summary>
    public static string BuildSkillCheckRules()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MANDATORY ROLL TRIGGERS - always emit a RequestRoll (with a label, a formula such as \"1d20\", and a dc) for:");
        sb.AppendLine("- Any physical challenge: climbing, jumping, swimming, lifting, forcing doors, breaking objects.");
        sb.AppendLine("- Any stealth action: hiding, sneaking, moving silently.");
        sb.AppendLine("- Any social challenge against an unwilling target: persuasion, deception, intimidation.");
        sb.AppendLine("- Any search or investigation: finding hidden objects, detecting traps, reading ancient text.");
        sb.AppendLine("- Any magical challenge: resisting spells, identifying magic, arcane knowledge checks.");
        sb.AppendLine("- Any awareness or detection scenario: entering a space where NPCs are present, checking whether you have been noticed, attempting to observe someone without being seen, or moving through an area where hostile or wary creatures could detect you. These require a Dexterity (Stealth) check. The DC is set by the situation - distracted NPCs DC 8, alert NPCs DC 14, supernaturally aware entities DC 18. NEVER narrate whether the player is noticed or unnoticed without a roll. Ask for the Stealth check first.");
        sb.AppendLine("- Any combat action.");
        sb.AppendLine("NEVER auto-succeed these actions, whatever the context. The player must roll, and the outcome depends on their result.");
        sb.AppendLine();
        sb.AppendLine("NARRATE THEN ROLL - do not narrate the outcome before the roll. Narrate the attempt and the situation, emit the RequestRoll, then stop. Wait for the player to report the result before narrating success or failure.");
        sb.AppendLine("WRONG: Player: \"I climb the wall\" -> AI: \"You grip the stones and haul yourself up successfully, reaching the top.\" (no roll requested)");
        sb.AppendLine("CORRECT: Player: \"I climb the wall\" -> AI: \"The frost-slicked stones offer uncertain handholds as you reach for the breach. The cold wind pulls at you. Roll Athletics (DC 12) to make the climb.\" and a RequestRoll with label \"Athletics check\", formula \"1d20\", dc 12.");
        sb.AppendLine();
        sb.AppendLine("DC GUIDELINES:");
        sb.AppendLine("- Easy task: DC 8");
        sb.AppendLine("- Moderate task: DC 12");
        sb.AppendLine("- Hard task: DC 16");
        sb.AppendLine("- Very hard task: DC 20");
        sb.AppendLine("- Nearly impossible: DC 24");
        sb.AppendLine("Examples: climbing a damaged, frost-covered wall in darkness = DC 14. Hiding from supernatural entities with keen senses = DC 14. Sneaking past distracted spirits = DC 10.");
        sb.AppendLine("Set each DC from the difficulty of the task and the situation described in the adventure. Do not default to the lowest DC.");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the system prompt that instructs the AI on how to act as a DM.
    /// </summary>
    public static string BuildSystemPrompt(Application.DTOs.Campaigns.CampaignDto campaign)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an AI Dungeon Master for a tabletop RPG session.");
        sb.AppendLine($"Game System: {campaign.GameSystem}");
        sb.AppendLine($"Ruleset: {campaign.Ruleset}");
        sb.AppendLine($"AI Role: {campaign.AiRole}");
        sb.AppendLine();
        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("- Respond to the player's action with engaging narration.");
        sb.AppendLine("- Use the retrieved rules and adventure context to ground your response.");
        sb.AppendLine("- If the action requires a dice roll, include a RequestRoll proposed action.");
        sb.AppendLine("- RequestRoll MUST include a 'label' field describing exactly what is being rolled, e.g. \"label\": \"Dexterity (Stealth) check\". A RequestRoll without a label will be rejected. Always include the skill or ability name and the check type in the label. It also needs a 'formula' such as \"1d20\".");
        sb.AppendLine("- If the action results in damage, healing, or condition changes, include the appropriate proposed actions.");
        sb.AppendLine("- Keep narration concise but immersive.");
        sb.AppendLine("- Do not reveal hidden information unless the player's action warrants it.");
        sb.AppendLine("- Cite rules sources when making rulings.");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT:");
        sb.AppendLine("Respond with ONE valid JSON object and nothing else: no markdown code fences, no text before or after it.");
        sb.AppendLine("Fields (use exactly these names, camelCase):");
        sb.AppendLine("- narration: string, required (public narration text; always a plain string, never an object)");
        sb.AppendLine("- privateDmNote: string or null (private note for the DM; a plain string)");
        sb.AppendLine("- rulesCitations: array of {sourceDocumentId (GUID), chunkId (GUID), summary (string)}; use [] when none");
        sb.AppendLine("- proposedActions: array of action objects; use [] when none. Each has a \"type\" (RequestRoll, ApplyDamage, ApplyHealing, ApplyCondition, RemoveCondition, RevealContent, MoveScene, etc.) plus the fields that type needs; all text fields are plain strings.");
        sb.AppendLine("Example of a valid response:");
        sb.AppendLine("{\"narration\":\"You push the door open. Dust drifts down from the rafters.\",\"privateDmNote\":null,\"rulesCitations\":[],\"proposedActions\":[]}");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a context string from the current campaign state.
    /// </summary>
    public static string BuildStateContext(Application.DTOs.CampaignState.CampaignStateDto state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CURRENT CAMPAIGN STATE:");

        if (!string.IsNullOrWhiteSpace(state.CurrentSceneJson))
        {
            sb.AppendLine($"Current Scene: {state.CurrentSceneJson}");
        }

        if (!string.IsNullOrWhiteSpace(state.StateJson))
        {
            sb.AppendLine($"State: {state.StateJson}");
        }

        return sb.ToString();
    }
}
