using System.Security.Cryptography;
using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
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
    private readonly ISessionHubNotifier? _hubNotifier;
    private readonly ISessionHubMembership? _hubMembership;

    /// <param name="hubNotifier">
    /// Announces joins, approvals and denials to the session's SignalR group. Optional so the service
    /// still works where no hub is registered (a failure to broadcast never fails the request).
    /// </param>
    /// <param name="hubMembership">
    /// Puts approvals, denials and the end of the session into effect on open hub connections. Optional
    /// for the same reason.
    /// </param>
    public SessionHostingService(
        AircaneDbContext db,
        ITokenRevocationService tokenRevocation,
        IParticipantTokenService tokenService,
        ILogger<SessionHostingService> logger,
        ISessionHubNotifier? hubNotifier = null,
        ISessionHubMembership? hubMembership = null)
    {
        _db = db;
        _tokenRevocation = tokenRevocation;
        _tokenService = tokenService;
        _logger = logger;
        _hubNotifier = hubNotifier;
        _hubMembership = hubMembership;
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
            status: SessionStatus.Active,
            requireHostApproval: request.RequireHostApproval);

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
    public async Task<ParticipantDto?> GetParticipantAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _db.SessionParticipants
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.Id == participantId, cancellationToken);

        return participant is null ? null : ToParticipantDto(participant);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParticipantDto>> GetParticipantsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var participants = await _db.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && !p.IsDenied && p.ReplacedByParticipantId == null)
            .OrderBy(p => p.JoinedAt)
            .ToListAsync(cancellationToken);

        return participants.Select(p => ToParticipantDto(p, SameNameAs(p, participants))).ToList();
    }

    /// <inheritdoc />
    public async Task<JoinSessionResult> JoinSessionAsync(
        JoinSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The secret is optional; one that is sent must be usable, or the browser would believe it can come back.
        string? secretHash = null;
        if (request.RejoinSecret is not null)
        {
            secretHash = RejoinSecret.TryHash(request.RejoinSecret)
                ?? throw new ArgumentException("The rejoin secret must be base64url text of at least 128 random bits.");
        }

        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{request.SessionId}' not found.");

        if (session.Status != SessionStatus.Active)
            throw new InvalidOperationException($"Session '{request.SessionId}' is not active.");

        var expectedHash = HashInviteCode(request.InviteCode);
        if (!string.Equals(session.InviteCodeHash, expectedHash, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid invite code.");

        // Everyone already here: to keep a secret to one seat, and to tell the host about a repeated name.
        var present = await _db.SessionParticipants
            .Where(p => p.SessionId == request.SessionId && p.ReplacedByParticipantId == null)
            .ToListAsync(cancellationToken);

        if (secretHash is not null && MatchSecret(present, secretHash) is { } holder)
        {
            // A seat this browser already has is taken back with rejoin, never by joining again. A declined
            // request's secret is only kept to say so; it moves to this new request.
            if (!holder.IsDenied)
                throw new RejoinSecretInUseException();
            holder.RejoinSecretHash = null;
        }

        // The host asked to approve each joiner (the default), or let anyone with the code straight in.
        var autoApproved = !session.RequireHostApproval;
        var participant = new SessionParticipant(
            sessionId: request.SessionId,
            displayName: request.DisplayName,
            role: ParticipantRole.Player,
            isApproved: autoApproved)
        {
            RejoinSecretHash = secretHash,
        };

        _db.SessionParticipants.Add(participant);
        await _db.SaveChangesAsync(cancellationToken);

        var issued = _tokenService.IssueToken(
            sessionId: request.SessionId,
            participantId: participant.Id,
            displayName: participant.DisplayName,
            role: participant.Role.ToString());

        // Record the issued token's id and expiry so ending the session can revoke this
        // exact token in the persistent store.
        participant.TokenId = issued.TokenId;
        participant.TokenExpiresAt = issued.ExpiresAt;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Participant joined session {SessionId}: {DisplayName} ({ParticipantId}), approved: {IsApproved}",
            request.SessionId, request.DisplayName, participant.Id, participant.IsApproved);

        // Tell the host (and the table) someone joined; the roster DTO carries no token, invite code or secret.
        var others = present.Where(p => !p.IsDenied).Append(participant).ToList();
        await NotifyAsync(hub => hub.NotifyParticipantJoinedAsync(
            request.SessionId, ToParticipantDto(participant, SameNameAs(participant, others)), cancellationToken));
        if (autoApproved)
        {
            await NotifyAsync(hub => hub.NotifyParticipantApprovedAsync(
                request.SessionId,
                new ParticipantApprovedNotification(request.SessionId, participant.Id, participant.DisplayName, AutoApproved: true),
                cancellationToken));
        }

        return new JoinSessionResult(
            ParticipantId: participant.Id,
            DisplayName: participant.DisplayName,
            Role: participant.Role,
            IsApproved: participant.IsApproved,
            ParticipantToken: issued.Token);
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

        // A denied request has no valid token any more; approving it would bring back a participant
        // who cannot act. They must join again.
        if (participant.IsDenied)
        {
            throw new InvalidOperationException(
                "That join request was denied. The person has to join again with the invite code before they can be approved.");
        }

        var wasApproved = participant.IsApproved;
        participant.IsApproved = true;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Participant approved: {ParticipantId} in session {SessionId}",
            participantId, sessionId);

        if (!wasApproved)
        {
            // Into the table's group first, so the approval itself reaches them there.
            await ChangeMembershipAsync(m => m.AdmitParticipantAsync(sessionId, participant.Id, cancellationToken));
            await NotifyAsync(hub => hub.NotifyParticipantApprovedAsync(
                sessionId,
                new ParticipantApprovedNotification(sessionId, participant.Id, participant.DisplayName, AutoApproved: false),
                cancellationToken));
        }

        return ToParticipantDto(participant);
    }

    /// <inheritdoc />
    public async Task<ParticipantDto> DenyParticipantAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _db.SessionParticipants
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.Id == participantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Participant '{participantId}' not found in session '{sessionId}'.");

        if (participant.IsDenied)
            return ToParticipantDto(participant);

        if (participant.IsApproved)
        {
            throw new InvalidOperationException(
                "That player is already approved and taking part. Only a pending join request can be denied.");
        }

        // Mark first, so a request that arrives while the token is being revoked is already explained.
        participant.IsDenied = true;
        await _db.SaveChangesAsync(cancellationToken);

        // Stop the token working for this session. Every request is checked against the revoked list
        // (see the JWT bearer events in Program.cs), so this takes effect on the next request.
        if (!string.IsNullOrWhiteSpace(participant.TokenId))
        {
            var expiresAt = participant.TokenExpiresAt ?? DateTimeOffset.UtcNow.AddDays(7);
            await _tokenRevocation.RevokeTokensAsync(
                [new RevokedTokenRecord(participant.TokenId, sessionId, expiresAt)], cancellationToken);
        }

        _logger.LogInformation(
            "Participant denied: {ParticipantId} in session {SessionId}", participantId, sessionId);

        await NotifyAsync(hub => hub.NotifyParticipantDeniedAsync(
            sessionId,
            new ParticipantDeniedNotification(sessionId, participant.Id, participant.DisplayName),
            cancellationToken));

        // Then close their connections: told first, so a waiting page can say why it stopped.
        await ChangeMembershipAsync(m => m.RemoveParticipantAsync(sessionId, participant.Id, cancellationToken));

        return ToParticipantDto(participant);
    }

    /// <inheritdoc />
    public async Task<RejoinAttempt> RejoinAsync(
        Guid sessionId,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var (session, participant) = await FindBySecretAsync(sessionId, secret, cancellationToken);
        if (session is null || participant is null)
            return RejoinAttempt.NotFound;
        if (participant.IsDenied)
            return RejoinAttempt.Denied;

        // A fresh token, and the one it replaces stops working: a seat has one live login.
        var previousTokenId = participant.TokenId;
        var previousExpiry = participant.TokenExpiresAt;
        var issued = _tokenService.IssueToken(
            sessionId: sessionId,
            participantId: participant.Id,
            displayName: participant.DisplayName,
            role: participant.Role.ToString());

        participant.TokenId = issued.TokenId;
        participant.TokenExpiresAt = issued.ExpiresAt;
        participant.LastSeenAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousTokenId))
        {
            await _tokenRevocation.RevokeTokensAsync(
                [new RevokedTokenRecord(previousTokenId, sessionId, previousExpiry ?? DateTimeOffset.UtcNow.AddDays(7))],
                cancellationToken);
        }

        _logger.LogInformation(
            "Participant {ParticipantId} took their seat in session {SessionId} back (approved: {IsApproved}); the previous token was revoked",
            participant.Id, sessionId, participant.IsApproved);

        return new RejoinAttempt(RejoinOutcome.Found, new RejoinSessionResult(
            ParticipantId: participant.Id,
            DisplayName: participant.DisplayName,
            Role: participant.Role,
            IsApproved: participant.IsApproved,
            Status: participant.IsApproved ? RejoinStatus.Approved : RejoinStatus.Pending,
            CharacterId: participant.CharacterId,
            SessionName: session.Name,
            ParticipantToken: issued.Token));
    }

    /// <inheritdoc />
    public async Task<RejoinStatusAttempt> GetRejoinStatusAsync(
        Guid sessionId,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var (session, participant) = await FindBySecretAsync(sessionId, secret, cancellationToken);
        if (session is null || participant is null)
            return RejoinStatusAttempt.NotFound;

        var status = participant.IsDenied ? RejoinStatus.Denied
            : participant.IsApproved ? RejoinStatus.Approved
            : RejoinStatus.Pending;
        return new RejoinStatusAttempt(
            participant.IsDenied ? RejoinOutcome.Denied : RejoinOutcome.Found,
            new RejoinStatusResult(session.Name, participant.DisplayName, status));
    }

    /// <inheritdoc />
    public async Task<ReplaceParticipantResult> ReplaceParticipantAsync(
        Guid sessionId,
        Guid oldParticipantId,
        Guid newParticipantId,
        CancellationToken cancellationToken = default)
    {
        if (oldParticipantId == newParticipantId)
            throw new InvalidOperationException("A participant cannot take over its own seat.");

        var session = await _db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        if (session.Status == SessionStatus.Ended)
            throw new InvalidOperationException("This session has ended.");

        // The runs whose turn order may name the old participant. Their turn locks are held while the order is
        // rewritten, so a turn cannot pass to (or from) the old participant half way through.
        var runIds = await _db.AdventureSessions.AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.IsGroup && r.Status == AdventureSessionStatus.InProgress)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        runIds.Sort();

        var held = new List<SemaphoreSlim>();
        SessionParticipant oldSeat;
        SessionParticipant newSeat;
        var changedRuns = new List<Guid>();
        var tookTheTurn = false;
        try
        {
            foreach (var runId in runIds)
            {
                var gate = AdventureTurnLocks.For(runId);
                await gate.WaitAsync(cancellationToken);
                held.Add(gate);
            }

            var pair = await _db.SessionParticipants
                .Where(p => p.SessionId == sessionId && (p.Id == oldParticipantId || p.Id == newParticipantId))
                .ToListAsync(cancellationToken);
            oldSeat = pair.FirstOrDefault(p => p.Id == oldParticipantId)
                ?? throw new KeyNotFoundException($"Participant '{oldParticipantId}' not found in session '{sessionId}'.");
            newSeat = pair.FirstOrDefault(p => p.Id == newParticipantId)
                ?? throw new KeyNotFoundException($"Participant '{newParticipantId}' not found in session '{sessionId}'.");
            foreach (var participant in pair)
                await _db.Entry(participant).ReloadAsync(cancellationToken);

            if (newSeat.IsDenied)
                throw new InvalidOperationException("That join request was denied, so it cannot take over a seat. The person has to join again.");
            if (newSeat.IsApproved)
                throw new InvalidOperationException("That participant is already approved. Only a waiting join request can take over a seat.");
            if (newSeat.ReplacedByParticipantId is not null)
                throw new InvalidOperationException("That join request has already been replaced.");
            if (oldSeat.IsDenied || oldSeat.ReplacedByParticipantId is not null || !oldSeat.IsApproved)
                throw new InvalidOperationException("Only the seat of an approved participant can be replaced.");

            // The seat: approval and character move to the new participant.
            newSeat.IsApproved = true;
            if (oldSeat.CharacterId is { } characterId)
            {
                newSeat.CharacterId = characterId;
                oldSeat.CharacterId = null;
            }
            oldSeat.IsApproved = false;
            oldSeat.ReplacedByParticipantId = newSeat.Id;
            oldSeat.RejoinSecretHash = null;

            // The turn order: the new participant takes the old one's place, and the turn if the old one had it.
            var runs = await _db.AdventureSessions
                .Where(r => runIds.Contains(r.Id))
                .ToListAsync(cancellationToken);
            foreach (var run in runs)
            {
                await _db.Entry(run).ReloadAsync(cancellationToken);
                if (!run.TurnOrder.Contains(oldParticipantId))
                    continue;

                run.TurnOrder = run.TurnOrder.Select(id => id == oldParticipantId ? newParticipantId : id).ToList();
                if (run.ActiveParticipantId == oldParticipantId)
                {
                    run.ActiveParticipantId = newParticipantId;
                    // A pending roll was asked of the old connection: dropped, as when a turn passes (#44).
                    // The AI asks again if it still needs it.
                    run.PendingRollLabel = null;
                    run.PendingRollDC = null;
                    tookTheTurn = true;
                }
                run.LastActivityAt = DateTimeOffset.UtcNow;
                changedRuns.Add(run.Id);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            foreach (var gate in held)
                gate.Release();
        }

        // The old participant's login stops working.
        if (!string.IsNullOrWhiteSpace(oldSeat.TokenId))
        {
            await _tokenRevocation.RevokeTokensAsync(
                [new RevokedTokenRecord(oldSeat.TokenId, sessionId, oldSeat.TokenExpiresAt ?? DateTimeOffset.UtcNow.AddDays(7))],
                cancellationToken);
        }

        _logger.LogInformation(
            "Participant {NewParticipantId} took over the seat of {OldParticipantId} in session {SessionId} (runs: {RunCount}, took the turn: {TookTheTurn})",
            newSeat.Id, oldSeat.Id, sessionId, changedRuns.Count, tookTheTurn);

        // The existing events: the new participant is approved (into the table's group first, so the approval
        // reaches them there), the old one left (and its connections are closed), and each run changed.
        await ChangeMembershipAsync(m => m.AdmitParticipantAsync(sessionId, newSeat.Id, cancellationToken));
        await NotifyAsync(hub => hub.NotifyParticipantApprovedAsync(
            sessionId,
            new ParticipantApprovedNotification(sessionId, newSeat.Id, newSeat.DisplayName, AutoApproved: false),
            cancellationToken));
        await NotifyAsync(hub => hub.NotifyParticipantLeftAsync(sessionId, oldSeat.Id, oldSeat.DisplayName, cancellationToken));
        await ChangeMembershipAsync(m => m.RemoveParticipantAsync(sessionId, oldSeat.Id, cancellationToken));
        foreach (var runId in changedRuns)
        {
            await NotifyAsync(hub => hub.NotifyAdventureStateChangedAsync(
                sessionId,
                new AdventureStateChangedNotification(runId, "ParticipantReplaced", DateTimeOffset.UtcNow),
                cancellationToken));
        }

        return new ReplaceParticipantResult(ToParticipantDto(newSeat), oldSeat.Id, changedRuns, tookTheTurn);
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

        // No way back into an ended session: its rejoin secrets go with it.
        var withSecrets = await _db.SessionParticipants
            .Where(p => p.SessionId == sessionId && p.RejoinSecretHash != null)
            .ToListAsync(cancellationToken);
        foreach (var participant in withSecrets)
            participant.RejoinSecretHash = null;

        await _db.SaveChangesAsync(cancellationToken);

        // Invalidate all participant tokens for this session. The in-memory session set is the
        // fast path; the persistent per-token store is authoritative across server restarts.
        _tokenRevocation.RevokeSession(sessionId);

        var activeTokens = await _db.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                && !p.IsDenied   // already revoked when they were denied
                && p.TokenId != null
                && p.TokenExpiresAt != null
                && p.TokenExpiresAt > DateTimeOffset.UtcNow)
            .Select(p => new RevokedTokenRecord(p.TokenId!, sessionId, p.TokenExpiresAt!.Value))
            .ToListAsync(cancellationToken);

        await _tokenRevocation.RevokeTokensAsync(activeTokens, cancellationToken);

        // Everyone's open connection to it, the host's included, is closed; the tokens are revoked, so no
        // reconnect gets back in.
        await ChangeMembershipAsync(m => m.CloseSessionAsync(sessionId, cancellationToken));

        _logger.LogInformation(
            "Session ended: {SessionId} at {EndedAt}. Revoked {TokenCount} participant token(s). Summary provided: {HasSummary}",
            sessionId, session.EndedAt, activeTokens.Count, summary is not null);

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

    // ── Rejoin and same-name helpers ──────────────────────────────────────────

    /// <summary>
    /// The session and the participant holding this rejoin secret, or nulls when there is nothing to give back
    /// (a malformed or unknown secret, an ended session, no such session). A replaced participant has no seat.
    /// </summary>
    private async Task<(Session? Session, SessionParticipant? Participant)> FindBySecretAsync(
        Guid sessionId, string? secret, CancellationToken cancellationToken)
    {
        var hash = RejoinSecret.TryHash(secret);
        if (hash is null)
            return (null, null);

        var session = await _db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || session.Status == SessionStatus.Ended)
            return (null, null);

        var holders = await _db.SessionParticipants
            .Where(p => p.SessionId == sessionId && p.RejoinSecretHash != null && p.ReplacedByParticipantId == null)
            .ToListAsync(cancellationToken);
        return (session, MatchSecret(holders, hash));
    }

    /// <summary>The participant whose stored hash equals <paramref name="hash"/>. Every row is compared in constant time.</summary>
    private static SessionParticipant? MatchSecret(IEnumerable<SessionParticipant> participants, string hash)
    {
        SessionParticipant? match = null;
        foreach (var participant in participants)
        {
            if (RejoinSecret.Matches(participant.RejoinSecretHash, hash) && match is null)
                match = participant;
        }
        return match;
    }

    /// <summary>
    /// For a pending request, the participant already in the session under the same display name (ignoring case
    /// and surrounding spaces): an approved one first, else the earliest earlier request. Null otherwise.
    /// </summary>
    private static Guid? SameNameAs(SessionParticipant participant, IReadOnlyList<SessionParticipant> present)
    {
        if (participant.IsApproved || participant.IsDenied)
            return null;

        var name = participant.DisplayName.Trim();
        return present
            .Where(p => p.Id != participant.Id && !p.IsDenied && p.ReplacedByParticipantId == null
                        && string.Equals(p.DisplayName.Trim(), name, StringComparison.OrdinalIgnoreCase)
                        && (p.IsApproved || p.JoinedAt < participant.JoinedAt))
            .OrderByDescending(p => p.IsApproved)
            .ThenBy(p => p.JoinedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefault();
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

    private static ParticipantDto ToParticipantDto(SessionParticipant p) => ToParticipantDto(p, sameNameAs: null);

    private static ParticipantDto ToParticipantDto(SessionParticipant p, Guid? sameNameAs) => new(
        Id: p.Id,
        SessionId: p.SessionId,
        DisplayName: p.DisplayName,
        Role: p.Role,
        CharacterId: p.CharacterId,
        IsApproved: p.IsApproved,
        JoinedAt: p.JoinedAt,
        LastSeenAt: p.LastSeenAt,
        IsDenied: p.IsDenied,
        SameNameAsParticipantId: sameNameAs,
        IsReplaced: p.ReplacedByParticipantId is not null);

    /// <summary>Applies a host decision to open hub connections. Best effort, like <see cref="NotifyAsync"/>.</summary>
    private async Task ChangeMembershipAsync(Func<ISessionHubMembership, Task> change)
    {
        if (_hubMembership is null)
            return;

        try
        {
            await change(_hubMembership);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to update hub connections for a session");
        }
    }

    /// <summary>Broadcasts to the play session's group. Best effort: a hub failure never fails the request.</summary>
    private async Task NotifyAsync(Func<ISessionHubNotifier, Task> send)
    {
        if (_hubNotifier is null)
            return;

        try
        {
            await send(_hubNotifier);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to broadcast a session lobby event");
        }
    }
}
