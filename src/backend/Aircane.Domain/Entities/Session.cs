using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// A play session under a campaign. Tracks access mode, invite code, and lifecycle status.
/// </summary>
public class Session : EntityBase
{
    public Guid CampaignId { get; init; }
    public string Name { get; set; }
    public SessionAccessMode AccessMode { get; init; }

    /// <summary>Hashed invite code. The raw code is never stored.</summary>
    public string InviteCodeHash { get; set; }

    public SessionStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Optional summary saved when the session ends.
    /// May contain a narrative recap, unresolved state notes, or host-provided notes.
    /// </summary>
    public string? Summary { get; set; }

    public Session(
        Guid campaignId,
        string name,
        SessionAccessMode accessMode,
        string inviteCodeHash,
        SessionStatus status = SessionStatus.Pending)
    {
        CampaignId = campaignId;
        Name = name;
        AccessMode = accessMode;
        InviteCodeHash = inviteCodeHash;
        Status = status;
    }

    // EF Core constructor
    private Session() : base()
    {
        Name = string.Empty;
        InviteCodeHash = string.Empty;
    }
}
