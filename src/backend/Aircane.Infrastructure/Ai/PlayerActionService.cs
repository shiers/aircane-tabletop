using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Enums;
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
    private readonly ILogger<PlayerActionService> _logger;

    public PlayerActionService(
        ICampaignService campaignService,
        ICampaignStateService campaignStateService,
        IRagContextBuilder ragContextBuilder,
        IAiProvider aiProvider,
        AiOutputParser outputParser,
        IStateCommandExecutor stateCommandExecutor,
        ILogger<PlayerActionService> logger)
    {
        _campaignService = campaignService;
        _campaignStateService = campaignStateService;
        _ragContextBuilder = ragContextBuilder;
        _aiProvider = aiProvider;
        _outputParser = outputParser;
        _stateCommandExecutor = stateCommandExecutor;
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

        // 4. Build AI prompt with state + context + player action
        var messages = BuildPromptMessages(request, campaign, state, ragResult);

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

        // 7. Map citations
        var citations = ragResult.Citations
            .Select(c => new PlayerActionCitation(
                SourceTitle: c.SourceDocumentTitle,
                PageNumber: c.PageNumber,
                SectionTitle: c.SectionTitle,
                ChunkId: c.ChunkId))
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
    /// Builds the prompt messages for the AI DM, including system instructions,
    /// campaign state, RAG context, and the player's action.
    /// </summary>
    public static IReadOnlyList<AiMessage> BuildPromptMessages(
        PlayerActionRequest request,
        Application.DTOs.Campaigns.CampaignDto campaign,
        Application.DTOs.CampaignState.CampaignStateDto state,
        RagContextResult ragResult)
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
