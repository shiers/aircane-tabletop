using Aircane.Application.Abstractions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Infrastructure.Chat;

/// <summary>
/// Stores and reads a play session's table chat (see <see cref="IChatService"/>).
/// <para>
/// <b>Retention:</b> a session keeps its newest <see cref="ChatMessage.MaxStoredPerSession"/> messages; older
/// ones are deleted as new ones arrive, so a session's chat is bounded whatever the length of the game. There is
/// no timed expiry: an ended session keeps its (at most that many) messages, as it keeps the rest of its record.
/// Nothing deletes sessions today (not even deleting their campaign); if one is ever deleted its chat goes with
/// it (cascade). Chat is table-wide, so every stored message is something every approved participant was shown.
/// </para>
/// </summary>
public sealed class ChatService : IChatService
{
    private readonly AircaneDbContext _db;

    public ChatService(AircaneDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<ChatMessageDto?> RecordAsync(
        Guid sessionId,
        Guid senderId,
        string senderName,
        ParticipantRole senderRole,
        string text,
        CancellationToken ct = default)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return null;
        if (trimmed.Length > ChatMessage.MaxTextLength)
            throw new ArgumentException($"A chat message can be at most {ChatMessage.MaxTextLength} characters.", nameof(text));

        var name = senderName.Length > 100 ? senderName[..100] : senderName;
        var message = new ChatMessage(sessionId, senderId, name, senderRole, trimmed);
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(ct);

        await TrimAsync(sessionId, ct);
        return ToDto(message);
    }

    /// <inheritdoc />
    public async Task<ChatHistoryDto> GetHistoryAsync(
        Guid sessionId,
        int limit = ChatHistory.DefaultLimit,
        Guid? before = null,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, ChatHistory.MaxLimit);

        var query = _db.ChatMessages.AsNoTracking().Where(m => m.SessionId == sessionId);

        if (before is { } beforeId)
        {
            // Keyset: strictly older than that message's time. (Two messages in the same tick, 100ns, are
            // not told apart; the id only orders them within a page.) A message the session does not have
            // (wrong id, already trimmed away) gives an empty page rather than the newest one.
            var cursorTime = await _db.ChatMessages.AsNoTracking()
                .Where(m => m.Id == beforeId && m.SessionId == sessionId)
                .Select(m => (DateTimeOffset?)m.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (cursorTime is null)
                return new ChatHistoryDto([], HasMore: false);

            query = query.Where(m => m.CreatedAt < cursorTime.Value);
        }

        // One extra row says whether there is an older page.
        var newestFirst = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = newestFirst.Count > limit;
        var page = newestFirst.Take(limit).Reverse().Select(ToDto).ToList();
        return new ChatHistoryDto(page, hasMore);
    }

    /// <summary>Deletes everything older than the newest <see cref="ChatMessage.MaxStoredPerSession"/> messages.</summary>
    private async Task TrimAsync(Guid sessionId, CancellationToken ct)
    {
        var count = await _db.ChatMessages.CountAsync(m => m.SessionId == sessionId, ct);
        if (count <= ChatMessage.MaxStoredPerSession)
            return;

        var excess = count - ChatMessage.MaxStoredPerSession;
        var oldest = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Take(excess)
            .ToListAsync(ct);
        _db.ChatMessages.RemoveRange(oldest);
        await _db.SaveChangesAsync(ct);
    }

    private static ChatMessageDto ToDto(ChatMessage m)
        => new(m.Id, m.SessionId, m.SenderId, m.SenderName, m.SenderRole, m.Text, m.CreatedAt);
}
