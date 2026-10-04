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

    /// <summary>The full registered set, in a deliberately shuffled order (Generic first).</summary>
    private static CharacterFormatDetector AllMappersDetector() =>
        new(new ICharacterSourceMapper[]
        {
            new GenericVttMapper(),
            new Roll20Mapper(),
            new FoundryPf2eMapper(),
            new FoundryDnd5eMapper(),
            new PathbuilderTwoMapper(),
        });

    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Theory]
    [InlineData("foundry-dnd5e.json", CharacterImportSource.FoundryDnd5e)]
    [InlineData("foundry-pf2e.json", CharacterImportSource.FoundryPf2e)]
    [InlineData("roll20.json", CharacterImportSource.Roll20)]
    [InlineData("pathbuilder2e.json", CharacterImportSource.PathbuilderTwo)]
    public void Detect_SpecificMappers_WinOverGeneric(string fixture, CharacterImportSource expected)
    {
        var detector = AllMappersDetector();
        using var doc = LoadFixture(fixture);

        var (source, mapper) = detector.Detect(doc);

        Assert.Equal(expected, source);
        Assert.NotNull(mapper);
        Assert.NotEqual(CharacterImportSource.GenericVtt, source);
    }

    [Fact]
    public void Detect_FoundryDnd5eAndPf2e_AreMutuallyExclusive()
    {
        var detector = AllMappersDetector();

        using (var dnd5e = LoadFixture("foundry-dnd5e.json"))
            Assert.Equal(CharacterImportSource.FoundryDnd5e, detector.Detect(dnd5e).Source);

        using (var pf2e = LoadFixture("foundry-pf2e.json"))
            Assert.Equal(CharacterImportSource.FoundryPf2e, detector.Detect(pf2e).Source);
    }

    [Fact]
    public void Detect_FallsBackToGeneric_ForUnidentifiedButNamedDocument()
    {
        var detector = AllMappersDetector();
        using var doc = LoadFixture("generic-vtt.json");

        var (source, _) = detector.Detect(doc);

        Assert.Equal(CharacterImportSource.GenericVtt, source);
    }
}
