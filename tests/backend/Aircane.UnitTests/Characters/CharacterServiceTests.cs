using Aircane.Application.Abstractions;
using Aircane.Application.Characters;
using Aircane.Application.DTOs.Characters;
using Aircane.Infrastructure.Characters;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Characters;

/// <summary>
/// Unit tests for <see cref="CharacterService"/> using an in-memory EF Core database.
/// </summary>
public class CharacterServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly CharacterService _sut;

    public CharacterServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _sut = new CharacterService(
            _db,
            new CharacterSchemaValidator(),
            new NullPdfCharacterExtractor(),
            NullLogger<CharacterService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildValidCanonicalJson(
        string name = "Aldric Stonehammer",
        string className = "Fighter",
        int level = 3,
        int hp = 28,
        int ac = 16) =>
        CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = name },
            Classes = [new CharacterClass { ClassName = className, Level = level, HitDie = 10 }],
            Abilities = new AbilityScores
            {
                Strength = 16, Dexterity = 12, Constitution = 14,
                Intelligence = 10, Wisdom = 11, Charisma = 9
            },
            Combat = new CombatStats
            {
                ArmorClass = ac,
                MaxHitPoints = hp,
                CurrentHitPoints = hp
            }
        });

    private static CreateCharacterRequest BuildCreateRequest(
        string name = "Aldric Stonehammer",
        string gameSystem = "D&D 5e",
        string ruleset = "2014",
        int level = 3,
        Guid? campaignId = null,
        Guid? ownerParticipantId = null) =>
        new(
            Name: name,
            GameSystem: gameSystem,
            Ruleset: ruleset,
            Level: level,
            CanonicalJson: BuildValidCanonicalJson(name: name, level: level),
            CampaignId: campaignId,
            OwnerParticipantId: ownerParticipantId);

    // ── CreateCharacterAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateCharacterAsync_ValidRequest_ReturnsDtoWithCorrectFields()
    {
        var campaignId = Guid.NewGuid();
        var request = BuildCreateRequest(campaignId: campaignId);

        var dto = await _sut.CreateCharacterAsync(request);

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("Aldric Stonehammer", dto.Name);
        Assert.Equal("D&D 5e", dto.GameSystem);
        Assert.Equal("2014", dto.Ruleset);
        Assert.Equal(3, dto.Level);
        Assert.Equal(campaignId, dto.CampaignId);
        Assert.NotEmpty(dto.CanonicalJson);
        Assert.NotEmpty(dto.CurrentStateJson);
    }

    [Fact]
    public async Task CreateCharacterAsync_PersistsCharacterToDatabase()
    {
        var request = BuildCreateRequest();

        var dto = await _sut.CreateCharacterAsync(request);

        var stored = await _db.Characters.FindAsync(dto.Id);
        Assert.NotNull(stored);
        Assert.Equal("Aldric Stonehammer", stored.Name);
    }

    [Fact]
    public async Task CreateCharacterAsync_WithoutCampaign_CampaignIdIsNull()
    {
        var request = BuildCreateRequest();

        var dto = await _sut.CreateCharacterAsync(request);

        Assert.Null(dto.CampaignId);
    }

    [Fact]
    public async Task CreateCharacterAsync_InvalidCanonicalJson_ThrowsArgumentException()
    {
        var request = new CreateCharacterRequest(
            Name: "Bad",
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            Level: 1,
            CanonicalJson: "not-valid-json");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateCharacterAsync(request));
    }

    [Fact]
    public async Task CreateCharacterAsync_CanonicalJsonFailsSchemaValidation_ThrowsArgumentException()
    {
        // Character with no classes — fails CharacterSchemaValidator
        var invalidCanonical = CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Test" },
            Classes = [], // empty — invalid
            Abilities = new AbilityScores(),
            Combat = new CombatStats { MaxHitPoints = 10, CurrentHitPoints = 10 }
        });

        var request = new CreateCharacterRequest(
            Name: "Test",
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            Level: 1,
            CanonicalJson: invalidCanonical);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateCharacterAsync(request));
    }

    [Fact]
    public async Task CreateCharacterAsync_SetsCurrentStateJsonFromCanonicalHp()
    {
        var canonicalJson = BuildValidCanonicalJson(hp: 30);
        var request = new CreateCharacterRequest(
            Name: "Aldric Stonehammer",
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            Level: 3,
            CanonicalJson: canonicalJson);

        var dto = await _sut.CreateCharacterAsync(request);

        var state = CharacterJsonSerializer.DeserializeState(dto.CurrentStateJson);
        Assert.NotNull(state);
        Assert.Equal(30, state.CurrentHitPoints);
    }

    // ── GetCharacterAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetCharacterAsync_ExistingId_ReturnsDto()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest());

        var dto = await _sut.GetCharacterAsync(created.Id);

        Assert.NotNull(dto);
        Assert.Equal(created.Id, dto.Id);
        Assert.Equal("Aldric Stonehammer", dto.Name);
    }

    [Fact]
    public async Task GetCharacterAsync_UnknownId_ReturnsNull()
    {
        var dto = await _sut.GetCharacterAsync(Guid.NewGuid());

        Assert.Null(dto);
    }

    // ── UpdateCharacterAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCharacterAsync_NameOnly_UpdatesName()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest());

        var updated = await _sut.UpdateCharacterAsync(
            created.Id,
            new UpdateCharacterRequest(Name: "Renamed Hero"));

        Assert.Equal("Renamed Hero", updated.Name);
        Assert.Equal(created.Level, updated.Level);
    }

    [Fact]
    public async Task UpdateCharacterAsync_LevelOnly_UpdatesLevel()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest(level: 3));

        var newCanonical = BuildValidCanonicalJson(level: 5);
        var updated = await _sut.UpdateCharacterAsync(
            created.Id,
            new UpdateCharacterRequest(Level: 5, CanonicalJson: newCanonical));

        Assert.Equal(5, updated.Level);
    }

    [Fact]
    public async Task UpdateCharacterAsync_ValidCanonicalJson_UpdatesCanonical()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest());
        var newCanonical = BuildValidCanonicalJson(name: "Updated Name", ac: 18);

        var updated = await _sut.UpdateCharacterAsync(
            created.Id,
            new UpdateCharacterRequest(CanonicalJson: newCanonical));

        Assert.NotEqual(created.CanonicalJson, updated.CanonicalJson);
    }

    [Fact]
    public async Task UpdateCharacterAsync_InvalidCanonicalJson_ThrowsArgumentException()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest());

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateCharacterAsync(
                created.Id,
                new UpdateCharacterRequest(CanonicalJson: "bad-json")));
    }

    [Fact]
    public async Task UpdateCharacterAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateCharacterAsync(
                Guid.NewGuid(),
                new UpdateCharacterRequest(Name: "X")));
    }

    [Fact]
    public async Task UpdateCharacterAsync_UpdatedAtIsRefreshed()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest());
        var originalUpdatedAt = created.UpdatedAt;

        await Task.Delay(10);
        var updated = await _sut.UpdateCharacterAsync(
            created.Id,
            new UpdateCharacterRequest(Name: "New Name"));

        Assert.True(updated.UpdatedAt >= originalUpdatedAt);
    }

    // ── DeleteCharacterAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCharacterAsync_ExistingId_RemovesCharacter()
    {
        var created = await _sut.CreateCharacterAsync(BuildCreateRequest());

        await _sut.DeleteCharacterAsync(created.Id);

        var found = await _sut.GetCharacterAsync(created.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteCharacterAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.DeleteCharacterAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteCharacterAsync_DoesNotAffectOtherCharacters()
    {
        var campaignId = Guid.NewGuid();
        var keep = await _sut.CreateCharacterAsync(BuildCreateRequest("Keep", campaignId: campaignId));
        var remove = await _sut.CreateCharacterAsync(BuildCreateRequest("Remove", campaignId: campaignId));

        await _sut.DeleteCharacterAsync(remove.Id);

        var remaining = await _sut.ListByCampaignAsync(campaignId);
        Assert.Single(remaining);
        Assert.Equal(keep.Id, remaining[0].Id);
    }

    [Fact]
    public async Task DeleteCharacterAsync_UnassignsCharacterFromSessionParticipants()
    {
        // Arrange: create a character and two participants assigned to it
        var character = await _sut.CreateCharacterAsync(BuildCreateRequest());

        var sessionId = Guid.NewGuid();
        var participant1 = new Aircane.Domain.Entities.SessionParticipant(
            sessionId, "Player One", Aircane.Domain.Enums.ParticipantRole.Player,
            isApproved: true, characterId: character.Id);
        var participant2 = new Aircane.Domain.Entities.SessionParticipant(
            sessionId, "Player Two", Aircane.Domain.Enums.ParticipantRole.Player,
            isApproved: true, characterId: character.Id);
        // A third participant assigned to a different character — should be unaffected
        var otherCharacterId = Guid.NewGuid();
        var participant3 = new Aircane.Domain.Entities.SessionParticipant(
            sessionId, "Player Three", Aircane.Domain.Enums.ParticipantRole.Player,
            isApproved: true, characterId: otherCharacterId);

        _db.SessionParticipants.AddRange(participant1, participant2, participant3);
        await _db.SaveChangesAsync();

        // Act
        await _sut.DeleteCharacterAsync(character.Id);

        // Assert: participants assigned to the deleted character are unassigned
        var p1 = await _db.SessionParticipants.FindAsync(participant1.Id);
        var p2 = await _db.SessionParticipants.FindAsync(participant2.Id);
        var p3 = await _db.SessionParticipants.FindAsync(participant3.Id);

        Assert.NotNull(p1);
        Assert.Null(p1.CharacterId);

        Assert.NotNull(p2);
        Assert.Null(p2.CharacterId);

        // Participant assigned to a different character is unaffected
        Assert.NotNull(p3);
        Assert.Equal(otherCharacterId, p3.CharacterId);
    }

    [Fact]
    public async Task DeleteCharacterAsync_NoParticipantsAssigned_DeletesSuccessfully()
    {
        // Arrange: character with no participants assigned
        var character = await _sut.CreateCharacterAsync(BuildCreateRequest());

        // Act & Assert: should not throw
        await _sut.DeleteCharacterAsync(character.Id);

        var found = await _sut.GetCharacterAsync(character.Id);
        Assert.Null(found);
    }

    // ── ListByCampaignAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListByCampaignAsync_NoCampaignCharacters_ReturnsEmptyList()
    {
        var result = await _sut.ListByCampaignAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListByCampaignAsync_MultipleCharacters_ReturnsAllForCampaign()
    {
        var campaignId = Guid.NewGuid();
        await _sut.CreateCharacterAsync(BuildCreateRequest("Alpha", campaignId: campaignId));
        await _sut.CreateCharacterAsync(BuildCreateRequest("Beta", campaignId: campaignId));
        // Character in a different campaign — should not appear
        await _sut.CreateCharacterAsync(BuildCreateRequest("Other", campaignId: Guid.NewGuid()));

        var result = await _sut.ListByCampaignAsync(campaignId);

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal(campaignId, c.CampaignId));
    }

    [Fact]
    public async Task ListByCampaignAsync_WithParticipantId_ReturnsOnlyOwnedCharacters()
    {
        var campaignId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var otherParticipantId = Guid.NewGuid();

        await _sut.CreateCharacterAsync(BuildCreateRequest("Mine", campaignId: campaignId, ownerParticipantId: participantId));
        await _sut.CreateCharacterAsync(BuildCreateRequest("Theirs", campaignId: campaignId, ownerParticipantId: otherParticipantId));

        var result = await _sut.ListByCampaignAsync(campaignId, participantId);

        Assert.Single(result);
        Assert.Equal("Mine", result[0].Name);
        Assert.Equal(participantId, result[0].OwnerParticipantId);
    }

    [Fact]
    public async Task ListByCampaignAsync_OrderedByName()
    {
        var campaignId = Guid.NewGuid();
        await _sut.CreateCharacterAsync(BuildCreateRequest("Zara", campaignId: campaignId));
        await _sut.CreateCharacterAsync(BuildCreateRequest("Aldric", campaignId: campaignId));
        await _sut.CreateCharacterAsync(BuildCreateRequest("Mira", campaignId: campaignId));

        var result = await _sut.ListByCampaignAsync(campaignId);

        Assert.Equal(3, result.Count);
        Assert.Equal("Aldric", result[0].Name);
        Assert.Equal("Mira", result[1].Name);
        Assert.Equal("Zara", result[2].Name);
    }

    // ── ImportCharacterFromJsonAsync ──────────────────────────────────────────

    private static ImportCharacterJsonRequest BuildImportRequest(
        string? canonicalJson = null,
        string gameSystem = "D&D 5e",
        string ruleset = "2014",
        Guid? campaignId = null,
        Guid? ownerParticipantId = null,
        string? originalFileName = null) =>
        new(
            CanonicalJson: canonicalJson ?? BuildValidCanonicalJson(),
            GameSystem: gameSystem,
            Ruleset: ruleset,
            CampaignId: campaignId,
            OwnerParticipantId: ownerParticipantId,
            OriginalFileName: originalFileName);

    [Fact]
    public async Task ImportCharacterFromJsonAsync_ValidJson_ReturnsSuccessResult()
    {
        var request = BuildImportRequest(originalFileName: "aldric.json");

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Character);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_ValidJson_PersistsCharacterToDatabase()
    {
        var request = BuildImportRequest();

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.True(result.Success);
        var stored = await _db.Characters.FindAsync(result.Character!.Id);
        Assert.NotNull(stored);
        Assert.Equal("Aldric Stonehammer", stored.Name);
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_ValidJson_CharacterDtoHasCorrectFields()
    {
        var campaignId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var request = BuildImportRequest(
            campaignId: campaignId,
            ownerParticipantId: participantId,
            originalFileName: "test.json");

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.True(result.Success);
        var dto = result.Character!;
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("Aldric Stonehammer", dto.Name);
        Assert.Equal("D&D 5e", dto.GameSystem);
        Assert.Equal("2014", dto.Ruleset);
        Assert.Equal(3, dto.Level);
        Assert.Equal(campaignId, dto.CampaignId);
        Assert.Equal(participantId, dto.OwnerParticipantId);
        Assert.NotEmpty(dto.CanonicalJson);
        Assert.NotEmpty(dto.CurrentStateJson);
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_MalformedJson_ReturnsFailureWithInvalidJsonError()
    {
        var request = BuildImportRequest(canonicalJson: "{ this is not valid json }");

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.False(result.Success);
        Assert.Null(result.Character);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.StartsWith("Invalid JSON:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_EmptyJson_ReturnsFailure()
    {
        var request = BuildImportRequest(canonicalJson: "");

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.False(result.Success);
        Assert.Null(result.Character);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_MissingCharacterName_ReturnsSchemaValidationErrors()
    {
        var invalidCanonical = CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "" }, // missing required name
            Classes = [new CharacterClass { ClassName = "Fighter", Level = 3, HitDie = 10 }],
            Abilities = new AbilityScores(),
            Combat = new CombatStats { MaxHitPoints = 28, CurrentHitPoints = 28 }
        });

        var request = BuildImportRequest(canonicalJson: invalidCanonical);

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.False(result.Success);
        Assert.Null(result.Character);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_NoClasses_ReturnsSchemaValidationErrors()
    {
        var invalidCanonical = CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Test Hero" },
            Classes = [], // empty — fails validation
            Abilities = new AbilityScores(),
            Combat = new CombatStats { MaxHitPoints = 10, CurrentHitPoints = 10 }
        });

        var request = BuildImportRequest(canonicalJson: invalidCanonical);

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_InvalidAbilityScore_ReturnsSchemaValidationErrors()
    {
        var invalidCanonical = CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Test Hero" },
            Classes = [new CharacterClass { ClassName = "Fighter", Level = 1, HitDie = 10 }],
            Abilities = new AbilityScores { Strength = 0 }, // 0 is below minimum of 1
            Combat = new CombatStats { MaxHitPoints = 10, CurrentHitPoints = 10 }
        });

        var request = BuildImportRequest(canonicalJson: invalidCanonical);

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Strength", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_MultipleValidationErrors_ReturnsAllErrors()
    {
        // Both name and classes are invalid
        var invalidCanonical = CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "" },
            Classes = [],
            Abilities = new AbilityScores(),
            Combat = new CombatStats { MaxHitPoints = 10, CurrentHitPoints = 10 }
        });

        var request = BuildImportRequest(canonicalJson: invalidCanonical);

        var result = await _sut.ImportCharacterFromJsonAsync(request);

        Assert.False(result.Success);
        Assert.True(result.Errors.Count >= 2, $"Expected at least 2 errors, got {result.Errors.Count}");
    }

    [Fact]
    public async Task ImportCharacterFromJsonAsync_ValidJson_DoesNotPersistOnValidationFailure()
    {
        var invalidCanonical = CharacterJsonSerializer.Serialize(new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "" },
            Classes = [],
            Abilities = new AbilityScores(),
            Combat = new CombatStats { MaxHitPoints = 10, CurrentHitPoints = 10 }
        });

        var request = BuildImportRequest(canonicalJson: invalidCanonical);
        var countBefore = _db.Characters.Count();

        await _sut.ImportCharacterFromJsonAsync(request);

        Assert.Equal(countBefore, _db.Characters.Count());
    }
}

/// <summary>
/// Stub implementation of <see cref="IPdfCharacterExtractor"/> for unit tests
/// that do not exercise PDF extraction paths.
/// </summary>
file sealed class NullPdfCharacterExtractor : IPdfCharacterExtractor
{
    public Task<PdfCharacterExtractionResult> ExtractAsync(Stream pdfStream, CancellationToken ct = default)
        => Task.FromResult(new PdfCharacterExtractionResult
        {
            IsOcrRequired = false,
            MappedCharacter = null,
            ExtractedFields = [],
            UnmappedFields = [],
            Warnings = []
        });
}
