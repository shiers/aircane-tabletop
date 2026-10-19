using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.UnitTests.TestDoubles;

/// <summary>
/// The participant read the adventure controllers use to check a Player's seat, answered from the test's
/// own database; every other member is not used by those controllers and throws.
/// </summary>
internal sealed class DbBackedSessionHosting : ISessionHostingService
{
    private readonly AircaneDbContext _db;

    public DbBackedSessionHosting(AircaneDbContext db) => _db = db;

    public async Task<ParticipantDto?> GetParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken = default)
    {
        var p = await _db.SessionParticipants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId && x.Id == participantId, cancellationToken);
        return p is null
            ? null
            : new ParticipantDto(p.Id, p.SessionId, p.DisplayName, p.Role, p.CharacterId, p.IsApproved, p.JoinedAt, p.LastSeenAt, p.IsDenied);
    }

    public Task<SessionDto?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<(SessionDto Session, string InviteCode)> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<JoinSessionResult> JoinSessionAsync(JoinSessionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ParticipantDto> ApproveParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ParticipantDto> DenyParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ParticipantDto> AssignCharacterAsync(Guid sessionId, Guid participantId, Guid characterId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ParticipantDto>> GetParticipantsAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<EndSessionResponse> EndSessionAsync(Guid sessionId, string? summary, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task PauseAiAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ResumeAiResponse> ResumeAiAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
