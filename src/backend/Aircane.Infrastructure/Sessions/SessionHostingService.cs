using System.Security.Cryptography;
using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Sessions;

/// <summary>
/// EF Core-backed implementation of <see cref="ISessionHostingService"/>.
/// Handles session creation, invite code generation, and the session lifecycle.
/// </summary>
public sealed class SessionHostingService : ISessionHostingService
{
    private const int InviteCodeLength = 8;
    private const string InviteCodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private readonly AircaneDbContext _db;
    private readonly ITokenRevocationService _tokenRevocation;
    private readonly IParticipantTokenService _tokenService;
    private readonly ILogger<SessionHostingService> _logger;

    public SessionHostingService(
        AircaneDbContext db,
        ITokenRevocationService tokenRevocation,
        IParticipantTokenService tokenService,
        ILogger<SessionHostingService> logger)
    {
        _db = db;
        _tokenRevocation = tokenRevocation;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(SessionDto Session, string InviteCode)> CreateSessionAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inviteCode = GenerateInviteCode();
        var inviteCodeHash = HashInviteCode(inviteCode);

        var session = new Session(
            campaignId: request.CampaignId,
            name: request.Name,
            accessMode: request.AccessMode,
            inviteCodeHash: inviteCodeHash,
            status: SessionStatus.Active);

        session.StartedAt = DateTimeOffset.UtcNow;

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Session created: {SessionId} '{Name}' under campaign {CampaignId}",
            session.Id, session.Name, session.CampaignId);

        var joinUrl = $"/join/{session.Id}";

        var dto = ToDto(session, participantCount: 0, inviteCode: inviteCode, joinUrl: joinUrl);
        return (dto, inviteCode);
    }

    /// <inheritdoc />
    public async Task<SessionDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return null;

        var participantCount = await _db.SessionParticipants
            .AsNoTracking()
            .CountAsync(p => p.SessionId == sessionId, cancellationToken);

        return ToDto(session, participantCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParticipantDto>> GetParticipantsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var participants = await _db.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.JoinedAt)
            .ToListAsync(cancellationToken);

        return participants.Select(ToParticipantDto).ToList();
    }

    /// <inheritdoc />
    public async Task<JoinSessionResult> JoinSessionAsync(
        JoinSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{request.SessionId}' not found.");

        if (session.Status != SessionStatus.Active)
            throw new InvalidOperationException($"Session '{request.SessionId}' is not active.");

        var expectedHash = HashInviteCode(request.InviteCode);
        if (!string.Equals(session.InviteCodeHash, expectedHash, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid invite code.");

        var participant = new SessionParticipant(
            sessionId: request.SessionId,
            displayName: request.DisplayName,
            role: ParticipantRole.Player,
            isApproved: false);

        _db.SessionParticipants.Add(participant);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Participant joined session {SessionId}: {DisplayName} ({ParticipantId})",
            request.SessionId, request.DisplayName, participant.Id);

        return new JoinSessionResult(
            ParticipantId: participant.Id,
            DisplayName: participant.DisplayName,
            Role: participant.Role,
            IsApproved: participant.IsApproved,
            ParticipantToken: _tokenService.IssueToken(
                sessionId: request.SessionId,
                participantId: participant.Id,
                displayName: participant.DisplayName,
                role: participant.Role.ToString()));
    }

    /// <inheritdoc />
    public async Task<ParticipantDto> ApproveParticipantAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _db.SessionParticipants
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.Id == participantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Participant '{participantId}' not found in session '{sessionId}'.");

        participant.IsApproved = true;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Participant approved: {ParticipantId} in session {SessionId}",
            participantId, sessionId);

        return ToParticipantDto(participant);
    }

    /// <inheritdoc />
    public async Task<ParticipantDto> AssignCharacterAsync(
        Guid sessionId,
        Guid participantId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _db.SessionParticipants
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.Id == participantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Participant '{participantId}' not found in session '{sessionId}'.");

        participant.CharacterId = characterId;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Character {CharacterId} assigned to participant {ParticipantId} in session {SessionId}",
            characterId, participantId, sessionId);

        return ToParticipantDto(participant);
    }

    /// <inheritdoc />
    public async Task<EndSessionResponse> EndSessionAsync(
        Guid sessionId,
        string? summary,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        if (session.Status == SessionStatus.Ended)
            throw new InvalidOperationException($"Session '{sessionId}' is already ended.");

        session.Status = SessionStatus.Ended;
        session.EndedAt = DateTimeOffset.UtcNow;
        session.Summary = summary;

        await _db.SaveChangesAsync(cancellationToken);

        // Invalidate all participant tokens for this session via the revocation list.
        _tokenRevocation.RevokeSession(sessionId);

        _logger.LogInformation(
            "Session ended: {SessionId} at {EndedAt}. Summary provided: {HasSummary}",
            sessionId, session.EndedAt, summary is not null);

        return new EndSessionResponse(
            SessionId: sessionId,
            EndedAt: session.EndedAt.Value,
            Summary: session.Summary);
    }

    /// <inheritdoc />
    public async Task PauseAiAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        session.Status = SessionStatus.Paused;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("AI paused for session: {SessionId}", sessionId);
    }

    /// <inheritdoc />
    public async Task<ResumeAiResponse> ResumeAiAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        if (session.Status == SessionStatus.Ended)
            throw new InvalidOperationException($"Session '{sessionId}' has ended and cannot be resumed.");

        // Restore the AI to the campaign's configured role and authority.
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.CampaignId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign '{session.CampaignId}' not found for session '{sessionId}'.");

        session.Status = SessionStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "AI resumed for session {SessionId}. Restored role: {AiRole}, authority: {AiAuthority}",
            sessionId, campaign.AiRole, campaign.AiAuthority);

        return new ResumeAiResponse(
            SessionId: sessionId,
            RestoredAiRole: campaign.AiRole,
            RestoredAiAuthority: campaign.AiAuthority);
    }

    // ── Invite Code Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random uppercase alphanumeric invite code.
    /// </summary>
    public static string GenerateInviteCode()
    {
        var result = new char[InviteCodeLength];
        var alphabetLength = InviteCodeAlphabet.Length;

        for (var i = 0; i < InviteCodeLength; i++)
        {
            // Use RandomNumberGenerator for cryptographic randomness
            var randomByte = RandomNumberGenerator.GetInt32(alphabetLength);
            result[i] = InviteCodeAlphabet[randomByte];
        }

        return new string(result);
    }

    /// <summary>
    /// Hashes an invite code with SHA-256. The plain code is never stored.
    /// </summary>
    public static string HashInviteCode(string inviteCode)
    {
        var bytes = Encoding.UTF8.GetBytes(inviteCode);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static SessionDto ToDto(
        Session s,
        int participantCount = 0,
        string? inviteCode = null,
        string? joinUrl = null) => new(
            Id: s.Id,
            CampaignId: s.CampaignId,
            Name: s.Name,
            AccessMode: s.AccessMode,
            Status: s.Status,
            StartedAt: s.StartedAt,
            EndedAt: s.EndedAt,
            CreatedAt: s.CreatedAt,
            ParticipantCount: participantCount,
            InviteCode: inviteCode,
            JoinUrl: joinUrl);

    private static ParticipantDto ToParticipantDto(SessionParticipant p) => new(
        Id: p.Id,
        SessionId: p.SessionId,
        DisplayName: p.DisplayName,
        Role: p.Role,
        CharacterId: p.CharacterId,
        IsApproved: p.IsApproved,
        JoinedAt: p.JoinedAt,
        LastSeenAt: p.LastSeenAt);
}
