using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Aircane.Application.Validation;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Aircane.Infrastructure.Sessions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionHostingService"/> and <see cref="CreateSessionValidator"/>.
/// Uses an in-memory EF Core database so no real PostgreSQL is required.
/// </summary>
public class SessionHostingServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly SessionHostingService _svc;
    private readonly IValidator<CreateSessionRequest> _validator;
    private readonly InMemoryTokenRevocationService _tokenRevocation;

    public SessionHostingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _tokenRevocation = new InMemoryTokenRevocationService();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "aircane-test-signing-key-at-least-32-chars-long",
                ["Jwt:ExpiryHours"] = "24",
            })
            .Build();

        var tokenService = new ParticipantTokenService(
            config, _tokenRevocation, NullLogger<ParticipantTokenService>.Instance);

        _svc = new SessionHostingService(_db, _tokenRevocation, tokenService, NullLogger<SessionHostingService>.Instance);
        _validator = new CreateSessionValidator();
    }

    public void Dispose() => _db.Dispose();

    // ── Invite code generation ────────────────────────────────────────────────

    [Fact]
    public void GenerateInviteCode_ProducesEightCharacters()
    {
        var code = SessionHostingService.GenerateInviteCode();

        Assert.Equal(8, code.Length);
    }

    [Fact]
    public void GenerateInviteCode_ContainsOnlyUppercaseAlphanumericCharacters()
    {
        // Run several times to reduce the chance of a lucky pass
        for (var i = 0; i < 50; i++)
        {
            var code = SessionHostingService.GenerateInviteCode();

            Assert.All(code, c =>
                Assert.True(
                    (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'),
                    $"Character '{c}' is not uppercase alphanumeric."));
        }
    }

    [Fact]
    public void GenerateInviteCode_ProducesUniqueCodesAcrossMultipleCalls()
    {
        var codes = Enumerable.Range(0, 20)
            .Select(_ => SessionHostingService.GenerateInviteCode())
            .ToHashSet();

        // With 36^8 ≈ 2.8 trillion possibilities, 20 codes should all be unique
        Assert.Equal(20, codes.Count);
    }

    // ── Invite code hashing ───────────────────────────────────────────────────

    [Fact]
    public void HashInviteCode_PlainCodeDiffersFromHash()
    {
        var code = SessionHostingService.GenerateInviteCode();
        var hash = SessionHostingService.HashInviteCode(code);

        Assert.NotEqual(code, hash);
    }

    [Fact]
    public void HashInviteCode_SameInputProducesSameHash()
    {
        const string code = "ABCD1234";

        var hash1 = SessionHostingService.HashInviteCode(code);
        var hash2 = SessionHostingService.HashInviteCode(code);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashInviteCode_DifferentInputsProduceDifferentHashes()
    {
        var hash1 = SessionHostingService.HashInviteCode("ABCD1234");
        var hash2 = SessionHostingService.HashInviteCode("ABCD1235");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashInviteCode_ProducesSha256HexString()
    {
        var hash = SessionHostingService.HashInviteCode("TESTCODE");

        // SHA-256 produces 32 bytes = 64 hex characters
        Assert.Equal(64, hash.Length);
        Assert.All(hash, c => Assert.True(
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'),
            $"Character '{c}' is not a valid hex digit."));
    }

    // ── Session creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_SetsStatusToActive()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: SessionAccessMode.LocalLan);

        var (dto, _) = await _svc.CreateSessionAsync(request);

        Assert.Equal(SessionStatus.Active, dto.Status);
    }

    [Fact]
    public async Task CreateSessionAsync_SetsStartedAtToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: SessionAccessMode.Solo);

        var (dto, _) = await _svc.CreateSessionAsync(request);

        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(dto.StartedAt);
        Assert.True(dto.StartedAt >= before);
        Assert.True(dto.StartedAt <= after);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsPlainInviteCode()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: SessionAccessMode.LocalLan);

        var (dto, inviteCode) = await _svc.CreateSessionAsync(request);

        Assert.NotNull(inviteCode);
        Assert.Equal(8, inviteCode.Length);
        Assert.NotNull(dto.InviteCode);
        Assert.Equal(inviteCode, dto.InviteCode);
    }

    [Fact]
    public async Task CreateSessionAsync_StoresHashNotPlainCode()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: SessionAccessMode.LocalLan);

        var (dto, inviteCode) = await _svc.CreateSessionAsync(request);

        var stored = await _db.Sessions.FindAsync(dto.Id);
        Assert.NotNull(stored);
        Assert.NotEqual(inviteCode, stored!.InviteCodeHash);

        // Verify the stored hash matches what we'd compute from the plain code
        var expectedHash = SessionHostingService.HashInviteCode(inviteCode);
        Assert.Equal(expectedHash, stored.InviteCodeHash);
    }

    [Fact]
    public async Task CreateSessionAsync_PersistsSessionToDatabase()
    {
        var campaignId = Guid.NewGuid();
        var request = new CreateSessionRequest(
            CampaignId: campaignId,
            Name: "Persisted Session",
            AccessMode: SessionAccessMode.Solo);

        var (dto, _) = await _svc.CreateSessionAsync(request);

        var stored = await _db.Sessions.FindAsync(dto.Id);
        Assert.NotNull(stored);
        Assert.Equal(campaignId, stored!.CampaignId);
        Assert.Equal("Persisted Session", stored.Name);
        Assert.Equal(SessionAccessMode.Solo, stored.AccessMode);
        Assert.Equal(SessionStatus.Active, stored.Status);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsJoinUrl()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: SessionAccessMode.LocalLan);

        var (dto, _) = await _svc.CreateSessionAsync(request);

        Assert.NotNull(dto.JoinUrl);
        Assert.Contains(dto.Id.ToString(), dto.JoinUrl);
    }

    // ── GetSession ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionAsync_ReturnsNullForUnknownId()
    {
        var result = await _svc.GetSessionAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSessionAsync_DoesNotReturnInviteCode()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: SessionAccessMode.LocalLan);

        var (created, _) = await _svc.CreateSessionAsync(request);

        var fetched = await _svc.GetSessionAsync(created.Id);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.InviteCode);
        Assert.Null(fetched.JoinUrl);
    }

    // ── CreateSessionRequest validation ──────────────────────────────────────

    [Fact]
    public async Task Validator_ValidRequest_PassesValidation()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Valid Session",
            AccessMode: SessionAccessMode.LocalLan);

        var result = await _validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validator_EmptyName_FailsValidation()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "",
            AccessMode: SessionAccessMode.LocalLan);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSessionRequest.Name));
    }

    [Fact]
    public async Task Validator_EmptyCampaignId_FailsValidation()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.Empty,
            Name: "Valid Session",
            AccessMode: SessionAccessMode.LocalLan);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSessionRequest.CampaignId));
    }

    [Fact]
    public async Task Validator_NameExceedsMaxLength_FailsValidation()
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: new string('A', 201),
            AccessMode: SessionAccessMode.LocalLan);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSessionRequest.Name));
    }

    [Theory]
    [InlineData(SessionAccessMode.Solo)]
    [InlineData(SessionAccessMode.LocalLan)]
    [InlineData(SessionAccessMode.InternetTunnel)]
    [InlineData(SessionAccessMode.Cloud)]
    public async Task Validator_AllValidAccessModes_PassValidation(SessionAccessMode mode)
    {
        var request = new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Test Session",
            AccessMode: mode);

        var result = await _validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    // ── EndSession ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EndSessionAsync_SetsStatusToEnded()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        await _svc.EndSessionAsync(created.Id, summary: null);

        var stored = await _db.Sessions.FindAsync(created.Id);
        Assert.Equal(Aircane.Domain.Enums.SessionStatus.Ended, stored!.Status);
    }

    [Fact]
    public async Task EndSessionAsync_SetsEndedAt()
    {
        var before = DateTimeOffset.UtcNow;

        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        var response = await _svc.EndSessionAsync(created.Id, summary: null);

        Assert.True(response.EndedAt >= before);
        Assert.True(response.EndedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EndSessionAsync_SavesSummary()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        const string summary = "The party defeated the goblin king.";
        var response = await _svc.EndSessionAsync(created.Id, summary: summary);

        Assert.Equal(summary, response.Summary);

        var stored = await _db.Sessions.FindAsync(created.Id);
        Assert.Equal(summary, stored!.Summary);
    }

    [Fact]
    public async Task EndSessionAsync_RevokesSessionTokens()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        Assert.False(_tokenRevocation.IsSessionRevoked(created.Id));

        await _svc.EndSessionAsync(created.Id, summary: null);

        Assert.True(_tokenRevocation.IsSessionRevoked(created.Id));
    }

    [Fact]
    public async Task EndSessionAsync_ThrowsForUnknownSession()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.EndSessionAsync(Guid.NewGuid(), summary: null));
    }

    [Fact]
    public async Task EndSessionAsync_ThrowsIfAlreadyEnded()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        await _svc.EndSessionAsync(created.Id, summary: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.EndSessionAsync(created.Id, summary: null));
    }

    // ── ResumeAi ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeAiAsync_SetsStatusBackToActive()
    {
        var campaign = new Aircane.Domain.Entities.Campaign(
            name: "Test Campaign",
            gameSystem: "D&D 5e",
            ruleset: "2014",
            aiRole: AiRole.FullDm,
            aiAuthority: Aircane.Domain.Enums.AiAuthority.AutoApplySafeActions);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: campaign.Id,
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        await _svc.PauseAiAsync(created.Id);

        var stored = await _db.Sessions.FindAsync(created.Id);
        Assert.Equal(Aircane.Domain.Enums.SessionStatus.Paused, stored!.Status);

        await _svc.ResumeAiAsync(created.Id);

        await _db.Entry(stored).ReloadAsync();
        Assert.Equal(Aircane.Domain.Enums.SessionStatus.Active, stored.Status);
    }

    [Fact]
    public async Task ResumeAiAsync_ReturnsRestoredCampaignAiConfig()
    {
        var campaign = new Aircane.Domain.Entities.Campaign(
            name: "Test Campaign",
            gameSystem: "D&D 5e",
            ruleset: "2014",
            aiRole: AiRole.CoDm,
            aiAuthority: Aircane.Domain.Enums.AiAuthority.AskBeforeApplying);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: campaign.Id,
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        await _svc.PauseAiAsync(created.Id);

        var response = await _svc.ResumeAiAsync(created.Id);

        Assert.Equal(AiRole.CoDm, response.RestoredAiRole);
        Assert.Equal(Aircane.Domain.Enums.AiAuthority.AskBeforeApplying, response.RestoredAiAuthority);
    }

    [Fact]
    public async Task ResumeAiAsync_ThrowsForUnknownSession()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.ResumeAiAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ResumeAiAsync_ThrowsIfSessionEnded()
    {
        var campaign = new Aircane.Domain.Entities.Campaign(
            name: "Test Campaign",
            gameSystem: "D&D 5e",
            ruleset: "2014");
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: campaign.Id,
            Name: "Session",
            AccessMode: SessionAccessMode.Solo));

        await _svc.EndSessionAsync(created.Id, summary: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.ResumeAiAsync(created.Id));
    }

    // ── JoinSession token issuance ────────────────────────────────────────────

    [Fact]
    public async Task JoinSessionAsync_ReturnsNonEmptyParticipantToken()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var result = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        Assert.NotNull(result.ParticipantToken);
        Assert.NotEmpty(result.ParticipantToken);
    }

    [Fact]
    public async Task JoinSessionAsync_TokenContainsThreeJwtSegments()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var result = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        var parts = result.ParticipantToken.Split('.');
        Assert.Equal(3, parts.Length);
    }

    // ── GetParticipants ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetParticipantsAsync_ReturnsEmptyListWhenNoParticipants()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var participants = await _svc.GetParticipantsAsync(created.Id);

        Assert.Empty(participants);
    }

    [Fact]
    public async Task GetParticipantsAsync_ReturnsAllParticipantsAfterJoin()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Gandalf",
            InviteCode: inviteCode));

        var participants = await _svc.GetParticipantsAsync(created.Id);

        Assert.Equal(2, participants.Count);
        Assert.Contains(participants, p => p.DisplayName == "Thorin");
        Assert.Contains(participants, p => p.DisplayName == "Gandalf");
    }

    [Fact]
    public async Task GetParticipantsAsync_ReturnsParticipantsOrderedByJoinedAt()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "First",
            InviteCode: inviteCode));

        await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Second",
            InviteCode: inviteCode));

        var participants = await _svc.GetParticipantsAsync(created.Id);

        Assert.Equal("First", participants[0].DisplayName);
        Assert.Equal("Second", participants[1].DisplayName);
    }

    // ── ApproveParticipant ────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveParticipantAsync_SetsIsApprovedToTrue()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        Assert.False(joinResult.IsApproved);

        var approved = await _svc.ApproveParticipantAsync(created.Id, joinResult.ParticipantId);

        Assert.True(approved.IsApproved);
    }

    [Fact]
    public async Task ApproveParticipantAsync_PersistsApprovalToDatabase()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        await _svc.ApproveParticipantAsync(created.Id, joinResult.ParticipantId);

        var stored = await _db.SessionParticipants.FindAsync(joinResult.ParticipantId);
        Assert.NotNull(stored);
        Assert.True(stored!.IsApproved);
    }

    [Fact]
    public async Task ApproveParticipantAsync_ReturnsCorrectParticipantDto()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        var dto = await _svc.ApproveParticipantAsync(created.Id, joinResult.ParticipantId);

        Assert.Equal(joinResult.ParticipantId, dto.Id);
        Assert.Equal(created.Id, dto.SessionId);
        Assert.Equal("Thorin", dto.DisplayName);
        Assert.True(dto.IsApproved);
    }

    [Fact]
    public async Task ApproveParticipantAsync_ThrowsForUnknownParticipant()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.ApproveParticipantAsync(created.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ApproveParticipantAsync_ThrowsWhenParticipantBelongsToDifferentSession()
    {
        var (session1, inviteCode1) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session 1",
            AccessMode: SessionAccessMode.LocalLan));

        var (session2, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session 2",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: session1.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode1));

        // Try to approve a participant from session1 using session2's ID
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.ApproveParticipantAsync(session2.Id, joinResult.ParticipantId));
    }

    // ── AssignCharacter ───────────────────────────────────────────────────────

    [Fact]
    public async Task AssignCharacterAsync_SetsCharacterIdOnParticipant()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        await _svc.ApproveParticipantAsync(created.Id, joinResult.ParticipantId);

        var characterId = Guid.NewGuid();
        var dto = await _svc.AssignCharacterAsync(created.Id, joinResult.ParticipantId, characterId);

        Assert.Equal(characterId, dto.CharacterId);
    }

    [Fact]
    public async Task AssignCharacterAsync_PersistsCharacterIdToDatabase()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        var characterId = Guid.NewGuid();
        await _svc.AssignCharacterAsync(created.Id, joinResult.ParticipantId, characterId);

        var stored = await _db.SessionParticipants.FindAsync(joinResult.ParticipantId);
        Assert.NotNull(stored);
        Assert.Equal(characterId, stored!.CharacterId);
    }

    [Fact]
    public async Task AssignCharacterAsync_CanReassignCharacterToSameParticipant()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        var firstCharacterId = Guid.NewGuid();
        await _svc.AssignCharacterAsync(created.Id, joinResult.ParticipantId, firstCharacterId);

        var secondCharacterId = Guid.NewGuid();
        var dto = await _svc.AssignCharacterAsync(created.Id, joinResult.ParticipantId, secondCharacterId);

        Assert.Equal(secondCharacterId, dto.CharacterId);
    }

    [Fact]
    public async Task AssignCharacterAsync_ThrowsForUnknownParticipant()
    {
        var (created, _) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.AssignCharacterAsync(created.Id, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task AssignCharacterAsync_ReturnsCorrectParticipantDto()
    {
        var (created, inviteCode) = await _svc.CreateSessionAsync(new CreateSessionRequest(
            CampaignId: Guid.NewGuid(),
            Name: "Session",
            AccessMode: SessionAccessMode.LocalLan));

        var joinResult = await _svc.JoinSessionAsync(new JoinSessionRequest(
            SessionId: created.Id,
            DisplayName: "Thorin",
            InviteCode: inviteCode));

        var characterId = Guid.NewGuid();
        var dto = await _svc.AssignCharacterAsync(created.Id, joinResult.ParticipantId, characterId);

        Assert.Equal(joinResult.ParticipantId, dto.Id);
        Assert.Equal(created.Id, dto.SessionId);
        Assert.Equal("Thorin", dto.DisplayName);
        Assert.Equal(characterId, dto.CharacterId);
    }
}
