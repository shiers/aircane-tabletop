using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Permissions;

/// <summary>
/// Result of a permission check.
/// </summary>
public sealed record PermissionCheckResult(
    bool IsAllowed,
    ParticipantRole Role,
    string? DenialReason = null);
