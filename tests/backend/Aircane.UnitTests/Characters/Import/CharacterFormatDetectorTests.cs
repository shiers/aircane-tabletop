using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class CharacterFormatDetectorTests
{
    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    /// <summary>A last-resort stub that matches anything, at the lowest precedence.</summary>
    private sealed class GenericStubMapper : ICharacterSourceMapper
    {
        public CharacterImportSource Source => CharacterImportSource.GenericVtt;
        public int Order => int.MaxValue;
        public bool CanMap(JsonDocument doc) => true;
        public SourceMapResult Map(JsonDocument doc) => new()
        {
            Character = new Application.Characters.CanonicalCharacter(),
            MappedFields = new Dictionary<string, string>(),
        };
    }

    /// <summary>A mapper whose CanMap always throws, used to prove exceptions are swallowed.</summary>
    private sealed class ThrowingMapper : ICharacterSourceMapper
    {
        public CharacterImportSource Source => CharacterImportSource.Roll20;
        public int Order => 0;
        public bool CanMap(JsonDocument doc) => throw new InvalidOperationException("boom");
        public SourceMapResult Map(JsonDocument doc) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Detect_PrefersSpecificMapper_OverGeneric_RegardlessOfRegistrationOrder()
    {
        // Register Generic FIRST so DI order would pick it if precedence were by registration.
        var mappers = new ICharacterSourceMapper[]
        {
            new GenericStubMapper(),
            new PathbuilderTwoMapper(),
        };
        var detector = new CharacterFormatDetector(mappers);

        using var doc = Parse("""{"build":{"class":"Fighter","ancestry":"Dwarf"}}""");
        var (source, mapper) = detector.Detect(doc);

        Assert.Equal(CharacterImportSource.PathbuilderTwo, source);
        Assert.IsType<PathbuilderTwoMapper>(mapper);
    }

    [Fact]
    public void Detect_FallsBackToGeneric_WhenNoSpecificMapperMatches()
    {
        var mappers = new ICharacterSourceMapper[]
        {
            new PathbuilderTwoMapper(),
            new GenericStubMapper(),
        };
        var detector = new CharacterFormatDetector(mappers);

        using var doc = Parse("""{"something":"else"}""");
        var (source, mapper) = detector.Detect(doc);

        Assert.Equal(CharacterImportSource.GenericVtt, source);
        Assert.IsType<GenericStubMapper>(mapper);
    }

    [Fact]
    public void Detect_SwallowsExceptionsFromCanMap_AndContinues()
    {
        var mappers = new ICharacterSourceMapper[]
        {
            new ThrowingMapper(),
            new PathbuilderTwoMapper(),
        };
        var detector = new CharacterFormatDetector(mappers);

        using var doc = Parse("""{"build":{"class":"Fighter","ancestry":"Dwarf"}}""");
        var (source, mapper) = detector.Detect(doc);

        Assert.Equal(CharacterImportSource.PathbuilderTwo, source);
        Assert.NotNull(mapper);
    }

    [Fact]
    public void Detect_ReturnsUnknown_WhenNothingMatches()
    {
        var mappers = new ICharacterSourceMapper[]
        {
            new PathbuilderTwoMapper(),
        };
        var detector = new CharacterFormatDetector(mappers);

        using var doc = Parse("""{"unrelated":true}""");
        var (source, mapper) = detector.Detect(doc);

        Assert.Equal(CharacterImportSource.Unknown, source);
        Assert.Null(mapper);
    }
}
