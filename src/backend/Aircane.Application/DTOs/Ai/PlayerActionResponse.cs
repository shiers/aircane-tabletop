using Aircane.Application.Abstractions;

namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// Response from the AI DM after processing a player action.
/// Contains narration for broadcast and the status of any proposed actions.
/// </summary>
public sealed record PlayerActionResponse(
    /// <summary>Public narration text to broadcast to session participants.</summary>
    string Narration,

    /// <summary>Private DM note visible only to the host. Null when not applicable.</summary>
    string? PrivateDmNote,

    /// <summary>Status of each proposed action (applied, queued, or failed).</summary>
    IReadOnlyList<ProposedActionResult> ProposedActions,

    /// <summary>Source citations used to ground the AI response.</summary>
    IReadOnlyList<PlayerActionCitation> Citations);

/// <summary>
/// The result of processing a single AI-proposed action.
/// </summary>
public sealed record ProposedActionResult(
    /// <summary>The action type that was proposed.</summary>
    AiActionType ActionType,

    /// <summary>Human-readable label for the action.</summary>
    string? Label,

    /// <summary>How the action was handled.</summary>
    StateCommandOutcome Outcome,

    /// <summary>Error message if the action failed validation or permission check.</summary>
    string? ErrorMessage = null,

    /// <summary>The proposal ID if the action was queued for approval.</summary>
    Guid? ProposalId = null);

/// <summary>
/// A citation reference included in a player action response.
/// </summary>
public sealed record PlayerActionCitation(
    /// <summary>The source document title.</summary>
    string SourceTitle,

    /// <summary>Page number within the source document, if available.</summary>
    int? PageNumber,

    /// <summary>Section title within the source document, if available.</summary>
    string? SectionTitle,

    /// <summary>The chunk ID for traceability.</summary>
    Guid ChunkId);
