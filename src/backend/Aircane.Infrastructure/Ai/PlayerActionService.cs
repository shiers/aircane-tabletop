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
        ILogger<PlayerActionService> logger)
    {
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

        // 3. Build RAG context (rules + adventure context)
        var ragRequest = new RagContextRequest(
            Query: request.ActionText,
            GameSystem: campaign.GameSystem,
            Ruleset: campaign.Ruleset,
            RequesterRole: ParticipantRole.Host, // AI DM has full visibility
            TopK: 20,
            MaxContextChars: 8000,
            MinRelevanceScore: 0.3);

        var ragResult = await _ragContextBuilder.BuildContextAsync(ragRequest, cancellationToken);

        // 3b. Build the live combat context block, if an encounter is active. This lets the AI DM
        // reference HP, conditions, and whose turn it is when narrating.
        var combatContext = await BuildCombatContextAsync(request.CampaignId, state, cancellationToken);

        // 4. Build AI prompt with state + combat + context + player action
        var messages = BuildPromptMessages(request, campaign, state, ragResult, combatContext);

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
            var result = await _stateCommandExecutor.ExecuteAsync(action, commandContext, cancellationToken);
            actionResults.Add(new ProposedActionResult(
                ActionType: action.Type,
                Label: action.Label,
                Outcome: result.Outcome,
                ErrorMessage: result.ErrorMessage,
                ProposalId: result.ProposalId));
        }

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
    public static IReadOnlyList<AiMessage> BuildPromptMessages(
        PlayerActionRequest request,
        Application.DTOs.Campaigns.CampaignDto campaign,
        Application.DTOs.CampaignState.CampaignStateDto state,
        RagContextResult ragResult,
        string? combatContext = null)
    {
        var messages = new List<AiMessage>();

        // System message with AI DM instructions
        messages.Add(AiMessage.System(BuildSystemPrompt(campaign)));

        // Campaign state context
        var stateContext = BuildStateContext(state);
        if (!string.IsNullOrWhiteSpace(stateContext))
        {
            messages.Add(AiMessage.System(stateContext));
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

        // Player action as user message
        var userMessage = $"[Character {request.CharacterId}] Player action: {request.ActionText}";
        messages.Add(AiMessage.User(userMessage));

        return messages;
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
        sb.AppendLine("- If the action results in damage, healing, or condition changes, include the appropriate proposed actions.");
        sb.AppendLine("- Keep narration concise but immersive.");
        sb.AppendLine("- Do not reveal hidden information unless the player's action warrants it.");
        sb.AppendLine("- Cite rules sources when making rulings.");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT:");
        sb.AppendLine("Respond with a JSON object containing:");
        sb.AppendLine("- narration: string (public narration text)");
        sb.AppendLine("- privateDmNote: string|null (private note for the DM)");
        sb.AppendLine("- rulesCitations: array of {sourceDocumentId, chunkId, summary}");
        sb.AppendLine("- proposedActions: array of structured actions (RequestRoll, ApplyDamage, ApplyHealing, ApplyCondition, RemoveCondition, RevealContent, MoveScene, etc.)");

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
