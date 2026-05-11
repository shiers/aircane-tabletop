namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// Request to ask a rules question with optional campaign/ruleset context.
/// </summary>
public sealed record RulesQuestionRequest(
    /// <summary>The rules question to ask.</summary>
    string Question,

    /// <summary>The game system to scope the question (e.g. "D&amp;D 5e 2014").</summary>
    string? GameSystem = null,

    /// <summary>The ruleset to scope the question (e.g. "PHB", "DMG").</summary>
    string? Ruleset = null,

    /// <summary>Optional campaign ID for campaign-specific house rules context.</summary>
    Guid? CampaignId = null);
