using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Characters;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

/// <summary>
/// Exercises the D&amp;D Beyond URL/id parsing (<see cref="DndBeyondUrlImportService.ParseCharacterId"/>)
/// directly. No network is involved.
/// </summary>
public sealed class DndBeyondUrlParseTests
{
    [Theory]
    [InlineData("1234567", "1234567")]
    [InlineData("https://www.dndbeyond.com/characters/1234567", "1234567")]
    [InlineData("https://www.dndbeyond.com/characters/1234567/", "1234567")]
    [InlineData("https://www.dndbeyond.com/characters/1234567/builder", "1234567")]
    [InlineData("https://character-service.dndbeyond.com/characters/42", "42")]
    [InlineData("http://dndbeyond.com/characters/987", "987")]
    public void ParseCharacterId_ValidInput_ReturnsNumericId(string input, string expected)
    {
        Assert.Equal(expected, DndBeyondUrlImportService.ParseCharacterId(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://www.dndbeyond.com/characters/")]
    [InlineData("https://www.dndbeyond.com/characters/abc")]
    [InlineData("https://www.dndbeyond.com/profile/someone")]
    [InlineData("not-a-url-at-all")]
    public void ParseCharacterId_InvalidInput_ThrowsInvalidUrl(string input)
    {
        var ex = Assert.Throws<DndBeyondImportException>(
            () => DndBeyondUrlImportService.ParseCharacterId(input));
        Assert.Equal(DndBeyondImportStatusKind.InvalidUrl, ex.StatusKind);
    }

    [Fact]
    public void ParseCharacterId_OverlongInput_ThrowsInvalidUrl()
    {
        var longInput = "https://www.dndbeyond.com/characters/" + new string('1', 2100);

        var ex = Assert.Throws<DndBeyondImportException>(
            () => DndBeyondUrlImportService.ParseCharacterId(longInput));
        Assert.Equal(DndBeyondImportStatusKind.InvalidUrl, ex.StatusKind);
    }
}
