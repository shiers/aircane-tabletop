using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// One line of a play session's table chat, kept so late joiners and reloaded clients can catch up.
/// <para>
/// Chat is table-wide: there are no private or host-only messages, so everything stored here is something
/// every approved participant was shown. The sender is whoever held the token that sent it; nothing in the
/// message's own body decides it. <see cref="EntityBase.CreatedAt"/> is the time it was sent.
/// </para>
/// </summary>
public class ChatMessage : EntityBase
{
    /// <summary>The longest message accepted, in characters.</summary>
    public const int MaxTextLength = 500;

    /// <summary>
    /// How many messages a session keeps. Older ones are deleted as new ones arrive, so a long game cannot
    /// grow the table without bound: this is the retention rule.
    /// </summary>
    public const int MaxStoredPerSession = 1000;

    public Guid SessionId { get; init; }

    /// <summary>
    /// The sender's participant id. The host has no participant record, so for the host this is the id its
    /// client chose (the same one broadcast with the message).
    /// </summary>
    public Guid SenderId { get; init; }

    public string SenderName { get; init; }

    public ParticipantRole SenderRole { get; init; }

    public string Text { get; init; }

    public ChatMessage(Guid sessionId, Guid senderId, string senderName, ParticipantRole senderRole, string text)
    {
        SessionId = sessionId;
        SenderId = senderId;
        SenderName = senderName;
        SenderRole = senderRole;
        Text = text;
    }

    // EF Core constructor
    private ChatMessage() : base()
    {
        SenderName = string.Empty;
        Text = string.Empty;
    }
}
