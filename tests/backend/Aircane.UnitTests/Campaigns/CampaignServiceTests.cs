using Aircane.Application.DTOs.Campaigns;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Campaigns;

/// <summary>
/// Unit tests for <see cref="CampaignService"/> using an in-memory EF Core database.
/// </summary>
public class CampaignServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly CampaignService _sut;

    public CampaignServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _sut = new CampaignService(_db, NullLogger<CampaignService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── CreateCampaignAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateCampaignAsync_ValidRequest_ReturnsDtoWithCorrectFields()
    {
        var request = new CreateCampaignRequest(
            Name: "The Lost Mines",
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            AiRole: AiRole.CoDm,
            AiAuthority: AiAuthority.AskBeforeApplying);

        var dto = await _sut.CreateCampaignAsync(request);

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("The Lost Mines", dto.Name);
        Assert.Equal("D&D 5e", dto.GameSystem);
        Assert.Equal("2014", dto.Ruleset);
        Assert.Equal(AiRole.CoDm, dto.AiRole);
        Assert.Equal(AiAuthority.AskBeforeApplying, dto.AiAuthority);
        Assert.Null(dto.ActiveAdventureId);
    }

    [Fact]
    public async Task CreateCampaignAsync_DefaultAiSettings_UsesAssistantAndSuggestOnly()
    {
        var request = new CreateCampaignRequest("Campaign", "D&D 5e", "2014");

        var dto = await _sut.CreateCampaignAsync(request);

        Assert.Equal(AiRole.Assistant, dto.AiRole);
        Assert.Equal(AiAuthority.SuggestOnly, dto.AiAuthority);
    }

    [Fact]
    public async Task CreateCampaignAsync_PersistsCampaignToDatabase()
    {
        var request = new CreateCampaignRequest("Persisted Campaign", "D&D 5e", "2014");

        var dto = await _sut.CreateCampaignAsync(request);

        var stored = await _db.Campaigns.FindAsync(dto.Id);
        Assert.NotNull(stored);
        Assert.Equal("Persisted Campaign", stored.Name);
    }

    // ── ListCampaignsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListCampaignsAsync_NoCampaigns_ReturnsEmptyList()
    {
        var result = await _sut.ListCampaignsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListCampaignsAsync_MultipleCampaigns_ReturnsAllOrderedByCreatedAtDescending()
    {
        await _sut.CreateCampaignAsync(new CreateCampaignRequest("Alpha", "D&D 5e", "2014"));
        await Task.Delay(5); // ensure distinct timestamps
        await _sut.CreateCampaignAsync(new CreateCampaignRequest("Beta", "D&D 5e", "2014"));

        var result = await _sut.ListCampaignsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Beta", result[0].Name);
        Assert.Equal("Alpha", result[1].Name);
    }

    // ── GetCampaignAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetCampaignAsync_ExistingId_ReturnsDto()
    {
        var created = await _sut.CreateCampaignAsync(new CreateCampaignRequest("Find Me", "D&D 5e", "2014"));

        var dto = await _sut.GetCampaignAsync(created.Id);

        Assert.NotNull(dto);
        Assert.Equal(created.Id, dto.Id);
        Assert.Equal("Find Me", dto.Name);
    }

    [Fact]
    public async Task GetCampaignAsync_UnknownId_ReturnsNull()
    {
        var dto = await _sut.GetCampaignAsync(Guid.NewGuid());

        Assert.Null(dto);
    }

    // ── UpdateCampaignAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCampaignAsync_NameOnly_UpdatesNameLeavesOtherFieldsUnchanged()
    {
        var created = await _sut.CreateCampaignAsync(
            new CreateCampaignRequest("Original", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl));

        var updated = await _sut.UpdateCampaignAsync(created.Id, new UpdateCampaignRequest(Name: "Renamed"));

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(AiRole.FullDm, updated.AiRole);
        Assert.Equal(AiAuthority.FullSessionControl, updated.AiAuthority);
    }

    [Fact]
    public async Task UpdateCampaignAsync_AllFields_UpdatesAll()
    {
        var created = await _sut.CreateCampaignAsync(
            new CreateCampaignRequest("Old", "D&D 5e", "2014"));

        var adventureId = Guid.NewGuid();
        var updated = await _sut.UpdateCampaignAsync(created.Id, new UpdateCampaignRequest(
            Name: "New",
            AiRole: AiRole.Hybrid,
            AiAuthority: AiAuthority.AutoApplySafeActions,
            ActiveAdventureId: adventureId));

        Assert.Equal("New", updated.Name);
        Assert.Equal(AiRole.Hybrid, updated.AiRole);
        Assert.Equal(AiAuthority.AutoApplySafeActions, updated.AiAuthority);
        Assert.Equal(adventureId, updated.ActiveAdventureId);
    }

    [Fact]
    public async Task UpdateCampaignAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateCampaignAsync(Guid.NewGuid(), new UpdateCampaignRequest(Name: "X")));
    }

    [Fact]
    public async Task UpdateCampaignAsync_UpdatedAtIsRefreshed()
    {
        var created = await _sut.CreateCampaignAsync(new CreateCampaignRequest("Camp", "D&D 5e", "2014"));
        var originalUpdatedAt = created.UpdatedAt;

        await Task.Delay(10); // ensure time advances
        var updated = await _sut.UpdateCampaignAsync(created.Id, new UpdateCampaignRequest(Name: "Camp 2"));

        Assert.True(updated.UpdatedAt >= originalUpdatedAt);
    }

    // ── DeleteCampaignAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCampaignAsync_ExistingId_RemovesCampaign()
    {
        var created = await _sut.CreateCampaignAsync(new CreateCampaignRequest("To Delete", "D&D 5e", "2014"));

        await _sut.DeleteCampaignAsync(created.Id);

        var found = await _sut.GetCampaignAsync(created.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteCampaignAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.DeleteCampaignAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteCampaignAsync_DoesNotAffectOtherCampaigns()
    {
        var keep = await _sut.CreateCampaignAsync(new CreateCampaignRequest("Keep", "D&D 5e", "2014"));
        var remove = await _sut.CreateCampaignAsync(new CreateCampaignRequest("Remove", "D&D 5e", "2014"));

        await _sut.DeleteCampaignAsync(remove.Id);

        var remaining = await _sut.ListCampaignsAsync();
        Assert.Single(remaining);
        Assert.Equal(keep.Id, remaining[0].Id);
    }

    // ── AiRole / AiAuthority enum coverage ───────────────────────────────────

    [Theory]
    [InlineData(AiRole.Assistant)]
    [InlineData(AiRole.CoDm)]
    [InlineData(AiRole.FullDm)]
    [InlineData(AiRole.Hybrid)]
    public async Task CreateCampaignAsync_AllAiRoles_ArePersistedCorrectly(AiRole role)
    {
        var dto = await _sut.CreateCampaignAsync(
            new CreateCampaignRequest("Camp", "D&D 5e", "2014", AiRole: role));

        Assert.Equal(role, dto.AiRole);
    }

    [Theory]
    [InlineData(AiAuthority.SuggestOnly)]
    [InlineData(AiAuthority.AskBeforeApplying)]
    [InlineData(AiAuthority.AutoApplySafeActions)]
    [InlineData(AiAuthority.FullSessionControl)]
    public async Task CreateCampaignAsync_AllAiAuthorities_ArePersistedCorrectly(AiAuthority authority)
    {
        var dto = await _sut.CreateCampaignAsync(
            new CreateCampaignRequest("Camp", "D&D 5e", "2014", AiAuthority: authority));

        Assert.Equal(authority, dto.AiAuthority);
    }
}
