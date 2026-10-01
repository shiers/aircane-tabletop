using Aircane.Application.DTOs.Permissions;
using Aircane.Domain.Enums;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Performs server-side authorization for every API endpoint and SignalR hub action.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks whether a participant is authorized to perform the specified action.
    /// </summary>
    Task<PermissionCheckResult> CheckAsync(
        PermissionCheckRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the role of a participant within a session.
    /// Returns null if the participant is not found or not approved.
    /// </summary>
    Task<ParticipantRole?> GetParticipantRoleAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken = default);
}
