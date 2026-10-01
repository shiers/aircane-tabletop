using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Response returned after AI is successfully resumed for a session.
/// Confirms the AI role and authority that have been restored.
/// </summary>
public sealed record ResumeAiResponse(
    Guid SessionId,
    AiRole RestoredAiRole,
    AiAuthority RestoredAiAuthority);
