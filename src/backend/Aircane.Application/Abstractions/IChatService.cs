using Aircane.Domain.Enums;

namespace Aircane.Application.Abstractions;

/// <summary>
/// A play session's table chat, kept so late joiners and reloaded clients can catch up. Chat is table-wide:
/// there are no private or host-only messages, so history never holds anything a participant was not shown.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Stores a message and returns it. The text is trimmed; empty text is not stored (returns null). After
    /// storing, the session keeps its newest <c>ChatMessage.MaxStoredPerSession</c> messages and drops older ones.
    /// </summary>
    /// <exception cref="ArgumentException">The text is longer than <c>ChatMessage.MaxTextLength</c>.</exception>
    Task<ChatMessageDto?> RecordAsync(
        Guid sessionId,
        Guid senderId,
        string senderName,
        ParticipantRole senderRole,
        string text,
        CancellationToken ct = default);

    /// <summary>
    /// The most recent messages of a session, oldest first within the page. <paramref name="before"/> is the
    /// id of a message returned earlier: only messages older than it are returned (keyset paging, newest page first).
    /// </summary>
    Task<ChatHistoryDto> GetHistoryAsync(
        Guid sessionId,
        int limit = ChatHistory.DefaultLimit,
        Guid? before = null,
        CancellationToken ct = default);
}

/// <summary>Page-size rules for chat history.</summary>
public static class ChatHistory
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;
}

/// <summary>One stored chat message.</summary>
/// <param name="SenderId">The sender's participant id (for the host, the id its client chose).</param>
public sealed record ChatMessageDto(
    Guid Id,
    Guid SessionId,
    Guid SenderId,
    string SenderName,
    ParticipantRole SenderRole,
    string Text,
    DateTimeOffset SentAt);

/// <summary>A page of chat history.</summary>
/// <param name="Messages">Oldest first.</param>
/// <param name="HasMore">Older messages exist: ask again with <c>before</c> set to the first message's id.</param>
public sealed record ChatHistoryDto(IReadOnlyList<ChatMessageDto> Messages, bool HasMore);
