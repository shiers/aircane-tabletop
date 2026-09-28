using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for <see cref="GameSystemCanonicalizer"/>. Verifies that free-text game system
/// values converge on a canonical <see cref="GameSystemDefinition.Name"/> when they match a known
/// definition by name or identifier (case/whitespace-insensitive), and are otherwise preserved.
/// </summary>
public class GameSystemCanonicalizerTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly GameSystemCanonicalizer _sut;

    public GameSystemCanonicalizerTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _sut = new GameSystemCanonicalizer(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedDefinitionAsync(
        string identifier,
        string name,
        bool isActive = true)
    {
        var def = new GameSystemDefinition(
            identifier: identifier,
            name: name,
            version: "1.0.0",
            schemaVersion: 1,
            license: "built-in")
        {
            IsActive = isActive,
        };
        _db.GameSystemDefinitions.Add(def);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Canonicalize_ExactNameMatch_ReturnsCanonicalName()
    {
        await SeedDefinitionAsync("pathfinder-2e-remaster", "Pathfinder 2e");

        var result = await _sut.CanonicalizeAsync("Pathfinder 2e");

        Assert.Equal("Pathfinder 2e", result);
    }

    [Theory]
    [InlineData("pathfinder 2e")]     // lower case
    [InlineData("PATHFINDER 2E")]     // upper case
    [InlineData("  Pathfinder 2e  ")] // leading/trailing whitespace
    [InlineData("Pathfinder   2e")]   // collapsed internal whitespace
    public async Task Canonicalize_NameMatchIgnoresCaseAndWhitespace_ReturnsCanonicalName(string input)
    {
        await SeedDefinitionAsync("pathfinder-2e-remaster", "Pathfinder 2e");

        var result = await _sut.CanonicalizeAsync(input);

        Assert.Equal("Pathfinder 2e", result);
    }

    [Fact]
    public async Task Canonicalize_IdentifierMatch_ReturnsCanonicalName()
    {
        await SeedDefinitionAsync("pathfinder-2e-remaster", "Pathfinder 2e");

        var result = await _sut.CanonicalizeAsync("Pathfinder-2e-Remaster");

        Assert.Equal("Pathfinder 2e", result);
    }

    [Fact]
    public async Task Canonicalize_NoMatch_ReturnsTrimmedInput()
    {
        await SeedDefinitionAsync("pathfinder-2e-remaster", "Pathfinder 2e");

        var result = await _sut.CanonicalizeAsync("  Some Unknown System  ");

        Assert.Equal("Some Unknown System", result);
    }

    [Fact]
    public async Task Canonicalize_MatchesOnlyActiveDefinitions()
    {
        // Inactive definition must not be used as a canonicalization target.
        await SeedDefinitionAsync("legacy-system", "Legacy System", isActive: false);

        var result = await _sut.CanonicalizeAsync("legacy system");

        // Falls through to trimmed passthrough since the only candidate is inactive.
        Assert.Equal("legacy system", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Canonicalize_EmptyOrWhitespaceInput_ReturnsInputUnchanged(string input)
    {
        await SeedDefinitionAsync("pathfinder-2e-remaster", "Pathfinder 2e");

        var result = await _sut.CanonicalizeAsync(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public async Task Canonicalize_NoDefinitionsSeeded_ReturnsTrimmedInput()
    {
        var result = await _sut.CanonicalizeAsync("  D&D 5e  ");

        Assert.Equal("D&D 5e", result);
    }
}
