using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for <see cref="SystemRegistry"/> using EF Core InMemory provider.
/// </summary>
public class SystemRegistryTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly SystemRegistry _registry;
    private readonly GameSystemDefinitionSerializer _serializer;

    public SystemRegistryTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _serializer = new GameSystemDefinitionSerializer();
        var validator = new GameSystemDefinitionValidator();
        _registry = new SystemRegistry(_db, _serializer, validator);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ── Helper Methods ────────────────────────────────────────────────────────

    private static GameSystemDefinition CreateValidDefinition(
        string identifier = "test-system",
        string name = "Test System",
        string version = "1.0.0")
    {
        return new GameSystemDefinition(identifier, name, version, 1, "user-created")
        {
            DiceConventions = new List<DiceConvention>
            {
                new()
                {
                    Name = "primary",
                    Type = DiceConventionType.SingleDieModifier,
                    Die = "d20",
                    Description = "Standard d20 roll"
                }
            },
            ResolutionRules = new List<ResolutionRule>
            {
                new()
                {
                    Name = "check",
                    Type = ResolutionRuleType.TargetNumber,
                    Roll = "primary",
                    Comparison = ">=",
                    TargetSource = "dc"
                }
            }
        };
    }

    private async Task<GameSystemDefinition> SeedDefinitionAsync(
        string identifier = "test-system",
        string name = "Test System",
        string version = "1.0.0")
    {
        var definition = CreateValidDefinition(identifier, name, version);
        await _registry.CreateAsync(definition);
        return definition;
    }

    // ── CreateAsync Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidDefinition_PersistsAndReturnsId()
    {
        var definition = CreateValidDefinition();

        var id = await _registry.CreateAsync(definition);

        Assert.NotEqual(Guid.Empty, id);
        var stored = await _db.GameSystemDefinitions.FindAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("test-system", stored.Identifier);
        Assert.Equal("Test System", stored.Name);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task CreateAsync_ValidDefinition_CreatesVersionRecord()
    {
        var definition = CreateValidDefinition();

        var id = await _registry.CreateAsync(definition);

        var versions = await _db.GameSystemDefinitionVersions
            .Where(v => v.GameSystemDefinitionId == id)
            .ToListAsync();
        Assert.Single(versions);
        Assert.Equal("1.0.0", versions[0].Version);
        Assert.False(string.IsNullOrWhiteSpace(versions[0].DefinitionJson));
    }

    [Fact]
    public async Task CreateAsync_ValidDefinition_SerializesDefinitionJson()
    {
        var definition = CreateValidDefinition();

        var id = await _registry.CreateAsync(definition);

        var stored = await _db.GameSystemDefinitions.FindAsync(id);
        Assert.NotNull(stored);
        Assert.NotEqual("{}", stored!.DefinitionJson);
        Assert.Contains("test-system", stored.DefinitionJson);
    }

    [Fact]
    public async Task CreateAsync_InvalidDefinition_ThrowsValidationException()
    {
        var definition = new GameSystemDefinition("", "", "", 1, "user-created");

        await Assert.ThrowsAsync<ValidationException>(
            () => _registry.CreateAsync(definition));
    }

    // ── GetByIdAsync Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingDefinition_ReturnsHydratedDefinition()
    {
        var definition = await SeedDefinitionAsync();

        var result = await _registry.GetByIdAsync(definition.Id);

        Assert.NotNull(result);
        Assert.Equal("test-system", result.Identifier);
        Assert.Equal("Test System", result.Name);
        // Verify hydration occurred
        Assert.NotEmpty(result.DiceConventions);
        Assert.Equal("primary", result.DiceConventions[0].Name);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _registry.GetByIdAsync(Guid.NewGuid()));
    }

    // ── GetByCampaignAsync Tests ──────────────────────────────────────────────

    [Fact]
    public async Task GetByCampaignAsync_CampaignWithDefinition_ReturnsDefinition()
    {
        var definition = await SeedDefinitionAsync();
        var campaign = new Campaign("Test Campaign", "Test", "1.0")
        {
            GameSystemDefinitionId = definition.Id
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var result = await _registry.GetByCampaignAsync(campaign.Id);

        Assert.NotNull(result);
        Assert.Equal(definition.Id, result.Id);
        Assert.NotEmpty(result.DiceConventions);
    }

    [Fact]
    public async Task GetByCampaignAsync_CampaignWithoutDefinition_ThrowsInvalidOperationException()
    {
        var campaign = new Campaign("Legacy Campaign", "D&D 5e", "2014");
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registry.GetByCampaignAsync(campaign.Id));
    }

    [Fact]
    public async Task GetByCampaignAsync_NonExistentCampaign_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _registry.GetByCampaignAsync(Guid.NewGuid()));
    }

    // ── ListAsync Tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_MultipleDefinitions_ReturnsOnlyActive()
    {
        await SeedDefinitionAsync("system-a", "System A");
        await SeedDefinitionAsync("system-b", "System B");

        // Deactivate one (no campaigns reference it)
        var definitions = await _db.GameSystemDefinitions.ToListAsync();
        var toDeactivate = definitions.First(d => d.Identifier == "system-a");
        toDeactivate.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await _registry.ListAsync();

        Assert.Single(result);
        Assert.Equal("System B", result[0].Name);
    }

    [Fact]
    public async Task ListAsync_ReturnsSummaryDtos()
    {
        await SeedDefinitionAsync("my-system", "My System", "2.0.0");

        var result = await _registry.ListAsync();

        Assert.Single(result);
        var summary = result[0];
        Assert.Equal("my-system", summary.Identifier);
        Assert.Equal("My System", summary.Name);
        Assert.Equal("2.0.0", summary.Version);
        Assert.Equal("user-created", summary.License);
        Assert.True(summary.IsActive);
        Assert.False(summary.IsBuiltIn);
    }

    // ── UpdateAsync Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ValidUpdate_UpdatesEntityAndCreatesNewVersion()
    {
        var definition = await SeedDefinitionAsync();

        var updated = CreateValidDefinition("test-system", "Test System Updated", "2.0.0");

        await _registry.UpdateAsync(definition.Id, updated);

        var stored = await _db.GameSystemDefinitions.FindAsync(definition.Id);
        Assert.Equal("Test System Updated", stored!.Name);
        Assert.Equal("2.0.0", stored.Version);

        var versions = await _db.GameSystemDefinitionVersions
            .Where(v => v.GameSystemDefinitionId == definition.Id)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal("1.0.0", versions[0].Version);
        Assert.Equal("2.0.0", versions[1].Version);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        var definition = CreateValidDefinition();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _registry.UpdateAsync(Guid.NewGuid(), definition));
    }

    [Fact]
    public async Task UpdateAsync_InvalidDefinition_ThrowsValidationException()
    {
        var definition = await SeedDefinitionAsync();
        var invalid = new GameSystemDefinition("", "", "", 1, "user-created");

        await Assert.ThrowsAsync<ValidationException>(
            () => _registry.UpdateAsync(definition.Id, invalid));
    }

    // ── DeactivateAsync Tests ─────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_NoReferencingCampaigns_SetsInactive()
    {
        var definition = await SeedDefinitionAsync();

        await _registry.DeactivateAsync(definition.Id);

        var stored = await _db.GameSystemDefinitions.FindAsync(definition.Id);
        Assert.False(stored!.IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_WithReferencingCampaigns_ThrowsInvalidOperationException()
    {
        var definition = await SeedDefinitionAsync();
        var campaign = new Campaign("Test Campaign", "Test", "1.0")
        {
            GameSystemDefinitionId = definition.Id
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registry.DeactivateAsync(definition.Id));
        Assert.Contains("1 campaign(s)", ex.Message);
    }

    [Fact]
    public async Task DeactivateAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _registry.DeactivateAsync(Guid.NewGuid()));
    }

    // ── ImportAsync Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_ValidJson_PersistsAndReturnsDefinition()
    {
        var definition = CreateValidDefinition("imported-system", "Imported System");
        var json = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await _registry.ImportAsync(stream, "json");

        Assert.NotNull(result);
        Assert.Equal("imported-system", result.Identifier);
        Assert.True(result.IsActive);

        var stored = await _db.GameSystemDefinitions
            .FirstOrDefaultAsync(d => d.Identifier == "imported-system");
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task ImportAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not valid json"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registry.ImportAsync(stream, "json"));
    }

    // ── ExportAsync Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_ExistingDefinition_ReturnsJsonStream()
    {
        var definition = await SeedDefinitionAsync();

        var stream = await _registry.ExportAsync(definition.Id, "json");

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        Assert.Contains("test-system", json);
        Assert.Contains("Test System", json);
    }

    [Fact]
    public async Task ExportAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _registry.ExportAsync(Guid.NewGuid(), "json"));
    }

    // ── ValidateAsync Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ValidDefinition_ReturnsValidResult()
    {
        var definition = CreateValidDefinition();
        var json = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await _registry.ValidateAsync(stream, "json");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_InvalidJson_ReturnsInvalidResult()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not json"));

        var result = await _registry.ValidateAsync(stream, "json");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotPersist()
    {
        var definition = CreateValidDefinition("validate-only", "Validate Only");
        var json = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _registry.ValidateAsync(stream, "json");

        var count = await _db.GameSystemDefinitions.CountAsync();
        Assert.Equal(0, count);
    }

    // ── Export/Import Round-Trip Test ──────────────────────────────────────────

    [Fact]
    public async Task ExportThenImport_ProducesEquivalentDefinition()
    {
        var original = await SeedDefinitionAsync("roundtrip-system", "Round Trip System", "1.0.0");

        // Export
        var exportStream = await _registry.ExportAsync(original.Id, "json");

        // Import (into a fresh registry to avoid duplicate key issues)
        var importOptions = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var importDb = new AircaneDbContext(importOptions);
        var importRegistry = new SystemRegistry(importDb, _serializer, new GameSystemDefinitionValidator());

        var imported = await importRegistry.ImportAsync(exportStream, "json");

        // Verify equivalence
        Assert.Equal(original.Identifier, imported.Identifier);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Version, imported.Version);
        Assert.Equal(original.SchemaVersion, imported.SchemaVersion);
        Assert.Equal(original.License, imported.License);
    }
}
