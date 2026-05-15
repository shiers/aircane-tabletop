using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.CampaignState;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Single entry point for AI-initiated state changes. Validates the command,
/// checks role permission and authority level, then either applies directly
/// to campaign state or queues as a proposal for host approval.
/// </summary>
public sealed class StateCommandExecutor : IStateCommandExecutor
{
    private readonly IReadOnlyDictionary<AiActionType, IStateCommandValidator> _validators;
    private readonly IAiRoleConfigurationService _roleService;
    private readonly IAiAuthorityService _authorityService;
    private readonly ICampaignStateService _stateService;
    private readonly IAiProposalService _proposalService;
    private readonly ILogger<StateCommandExecutor> _logger;

    public StateCommandExecutor(
        IEnumerable<IStateCommandValidator> validators,
        IAiRoleConfigurationService roleService,
        IAiAuthorityService authorityService,
        ICampaignStateService stateService,
        IAiProposalService proposalService,
        ILogger<StateCommandExecutor> logger)
    {
        _validators = validators.ToDictionary(v => v.ActionType);
        _roleService = roleService;
        _authorityService = authorityService;
        _stateService = stateService;
        _proposalService = proposalService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StateCommandResult> ExecuteAsync(
        AiProposedAction command,
        StateCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        // Step 1: Validate the command
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            _logger.LogWarning(
                "Command validation failed for {ActionType} in campaign {CampaignId}: {Error}",
                command.Type, context.CampaignId, validationError);
            return StateCommandResult.ValidationFailed(validationError);
        }

        // Step 2: Check role permission
        if (!_roleService.IsActionPermitted(context.AiRole, command.Type))
        {
            _logger.LogWarning(
                "AI role {Role} does not have permission for action {ActionType} in campaign {CampaignId}",
                context.AiRole, command.Type, context.CampaignId);
            return StateCommandResult.PermissionDenied(
                $"AI role '{context.AiRole}' does not have permission for action '{command.Type}'.");
        }

        // Step 3: Check authority level - auto-apply or queue for approval
        if (_authorityService.CanAutoApply(context.AiAuthority, command.Type))
        {
            return await ApplyDirectlyAsync(command, context, cancellationToken);
        }
        else
        {
            return await QueueForApprovalAsync(command, context, cancellationToken);
        }
    }

    private string? ValidateCommand(AiProposedAction command)
    {
        if (_validators.TryGetValue(command.Type, out var validator))
        {
            return validator.Validate(command);
        }

        // No specific validator registered - allow the command through.
        // This supports future action types without requiring a validator for each.
        return null;
    }

    private async Task<StateCommandResult> ApplyDirectlyAsync(
        AiProposedAction command,
        StateCommandContext context,
        CancellationToken cancellationToken)
    {
        var payloadJson = BuildPayloadJson(command);
        var commandType = command.Type.ToString();

        var request = new ApplyCommandRequest(
            CampaignId: context.CampaignId,
            CommandType: commandType,
            PayloadJson: payloadJson,
            ActorType: "AI",
            SessionId: context.SessionId);

        await _stateService.ApplyCommandAsync(request, cancellationToken);

        _logger.LogInformation(
            "Auto-applied {ActionType} command to campaign {CampaignId} (authority: {Authority})",
            command.Type, context.CampaignId, context.AiAuthority);

        return StateCommandResult.Applied();
    }

    private async Task<StateCommandResult> QueueForApprovalAsync(
        AiProposedAction command,
        StateCommandContext context,
        CancellationToken cancellationToken)
    {
        var request = new CreateProposalRequest(
            SessionId: context.SessionId,
            CampaignId: context.CampaignId,
            ProposedAction: command);

        var proposal = await _proposalService.CreateProposalAsync(request, cancellationToken);

        _logger.LogInformation(
            "Queued {ActionType} command as proposal {ProposalId} for campaign {CampaignId} (authority: {Authority})",
            command.Type, proposal.Id, context.CampaignId, context.AiAuthority);

        return StateCommandResult.Queued(proposal.Id);
    }

    /// <summary>
    /// Builds the JSON payload for the campaign state service based on the command type.
    /// Each command type maps to the payload format expected by CampaignStateService.ApplyCommandAsync.
    /// </summary>
    private static string BuildPayloadJson(AiProposedAction command)
    {
        var payload = command.Type switch
        {
            AiActionType.RequestRoll => new Dictionary<string, object?>
            {
                ["characterId"] = command.CharacterId?.ToString(),
                ["label"] = command.Label,
                ["formula"] = command.Formula,
                ["dc"] = command.Dc,
                ["visibility"] = command.Visibility ?? "public",
                ["reason"] = command.Reason,
            },
            AiActionType.ApplyDamage => new Dictionary<string, object?>
            {
                ["characterId"] = command.CharacterId?.ToString(),
                ["amount"] = command.Amount,
                ["reason"] = command.Reason,
            },
            AiActionType.ApplyHealing => new Dictionary<string, object?>
            {
                ["characterId"] = command.CharacterId?.ToString(),
                ["amount"] = command.Amount,
                ["reason"] = command.Reason,
            },
            AiActionType.ApplyCondition => new Dictionary<string, object?>
            {
                ["characterId"] = command.CharacterId?.ToString(),
                ["conditionName"] = command.ConditionName,
                ["reason"] = command.Reason,
            },
            AiActionType.RemoveCondition => new Dictionary<string, object?>
            {
                ["characterId"] = command.CharacterId?.ToString(),
                ["conditionName"] = command.ConditionName,
                ["reason"] = command.Reason,
            },
            AiActionType.RevealContent => new Dictionary<string, object?>
            {
                ["contentId"] = command.ContentId?.ToString(),
            },
            AiActionType.MoveScene => new Dictionary<string, object?>
            {
                ["sceneId"] = command.TargetSceneId?.ToString(),
            },
            _ => new Dictionary<string, object?>
            {
                ["actionType"] = command.Type.ToString(),
                ["label"] = command.Label,
                ["reason"] = command.Reason,
            },
        };

        return JsonSerializer.Serialize(payload);
    }
}
