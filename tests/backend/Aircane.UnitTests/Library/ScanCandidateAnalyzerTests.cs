using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Library;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for <see cref="ScanCandidateAnalyzer"/>: duplicate grouping, already-imported
/// detection, likely-not-rules flagging, and suggestion propagation. Pure, no database.
/// </summary>
public class ScanCandidateAnalyzerTests
{
    private static readonly IScanCandidateAnalyzer Sut = new ScanCandidateAnalyzer();

    private static DocumentSourceFile File(string name, long size = 1000) =>
        new(SourcePath: $"/books/{name}", FileName: name, LastModifiedUtc: DateTimeOffset.UtcNow, SizeBytes: size);

    private static ScanCandidateDto ByName(IReadOnlyList<ScanCandidateDto> list, string name) =>
        list.Single(c => c.FileName == name);

    [Fact]
    public void Analyze_ReturnsOneCandidatePerFileInOrder()
    {
        var files = new[] { File("a.pdf"), File("b.pdf"), File("c.pdf") };

        var result = Sut.Analyze(files, new HashSet<string>());

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, result.Select(c => c.FileName));
    }

    [Fact]
    public void Analyze_OcrVariants_FlaggedAsDuplicateVariant()
    {
        var files = new[]
        {
            File("DnD 5e Players Handbook (BnW OCR).pdf"),
            File("DnD 5e Players Handbook (Color OCR).pdf"),
        };

        var result = Sut.Analyze(files, new HashSet<string>());

        Assert.All(result, c => Assert.Contains(ScanCandidateFlag.DuplicateVariant, c.Flags));
        // Both share the same dedup key.
        Assert.Equal(result[0].DedupKey, result[1].DedupKey);
    }

    [Fact]
    public void Analyze_UniqueFiles_NotFlaggedAsDuplicate()
    {
        var files = new[] { File("Players Handbook.pdf"), File("Dungeon Masters Guide.pdf") };

        var result = Sut.Analyze(files, new HashSet<string>());

        Assert.All(result, c => Assert.DoesNotContain(ScanCandidateFlag.DuplicateVariant, c.Flags));
    }

    [Fact]
    public void Analyze_AlreadyInLibrary_FlaggedAsAlreadyImported()
    {
        var file = File("Players Handbook.pdf");
        var existingKey = FilenameNormalizer.ComputeDedupKey("Players Handbook.pdf");
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { existingKey };

        var result = Sut.Analyze(new[] { file }, existing);

        var candidate = result.Single();
        Assert.True(candidate.AlreadyImported);
        Assert.Contains(ScanCandidateFlag.AlreadyImported, candidate.Flags);
    }

    [Fact]
    public void Analyze_NotInLibrary_NotFlaggedAsAlreadyImported()
    {
        var result = Sut.Analyze(new[] { File("Players Handbook.pdf") }, new HashSet<string>());

        Assert.False(result.Single().AlreadyImported);
    }

    [Theory]
    [InlineData("Volo's Guide to Monsters - Maps.pdf")]
    [InlineData("Dungeon Master's Screen Reincarnated.pdf")]
    [InlineData("Monster Tokens Pack.pdf")]
    [InlineData("Curse of Strahd Tarokka Cards.pdf")]
    public void Analyze_AssetFiles_FlaggedAsLikelyNotRules(string name)
    {
        var result = Sut.Analyze(new[] { File(name) }, new HashSet<string>());

        Assert.Contains(ScanCandidateFlag.LikelyNotRules, result.Single().Flags);
        Assert.NotNull(result.Single().Reason);
    }

    [Theory]
    [InlineData("Players Handbook.pdf")]
    [InlineData("Dungeon Masters Guide.pdf")]
    [InlineData("Monster Manual.pdf")]
    public void Analyze_RulesFiles_NotFlaggedAsLikelyNotRules(string name)
    {
        var result = Sut.Analyze(new[] { File(name) }, new HashSet<string>());

        Assert.DoesNotContain(ScanCandidateFlag.LikelyNotRules, result.Single().Flags);
    }

    [Fact]
    public void Analyze_PropagatesTitleAndRulesetSuggestions()
    {
        var result = Sut.Analyze(
            new[] { File("DnD 5e Dungeon Masters Guide 2014 (Color OCR).pdf") },
            new HashSet<string>());

        var candidate = result.Single();
        Assert.Equal("D&D 5e Dungeon Masters Guide 2014", candidate.SuggestedTitle);
        Assert.Equal("2014", candidate.SuggestedRuleset);
    }

    [Fact]
    public void Analyze_CleanFile_HasNoFlagsAndNullReason()
    {
        var result = Sut.Analyze(new[] { File("Players Handbook.pdf") }, new HashSet<string>());

        var candidate = result.Single();
        Assert.Empty(candidate.Flags);
        Assert.Null(candidate.Reason);
    }

    // ── Content-hash exact-duplicate detection (B4.1) ────────────────────────────

    [Fact]
    public void Analyze_ContentHashMatchesExisting_FlaggedExactDuplicate()
    {
        var file = File("Some Random Name.pdf");
        var fileHashes = new Dictionary<string, string> { [file.SourcePath] = "abc123" };
        var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abc123" };

        var result = Sut.Analyze(
            new[] { file }, new HashSet<string>(), fileHashes, existingHashes);

        var candidate = result.Single();
        Assert.Contains(ScanCandidateFlag.ExactDuplicate, candidate.Flags);
        Assert.True(candidate.AlreadyImported);
    }

    [Fact]
    public void Analyze_ContentHashDoesNotMatch_NotFlaggedExactDuplicate()
    {
        var file = File("Unique.pdf");
        var fileHashes = new Dictionary<string, string> { [file.SourcePath] = "unique-hash" };
        var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "other-hash" };

        var result = Sut.Analyze(
            new[] { file }, new HashSet<string>(), fileHashes, existingHashes);

        Assert.DoesNotContain(ScanCandidateFlag.ExactDuplicate, result.Single().Flags);
    }

    // ── Configurable exclude patterns (B4.3) ─────────────────────────────────────

    [Fact]
    public void Analyze_CustomExcludePatterns_FlagMatchingFiles()
    {
        // A "battlemap" file wouldn't match the default signals, but a custom *battlemap* pattern does.
        var result = Sut.Analyze(
            new[] { File("Grand Battlemap Collection.pdf") },
            new HashSet<string>(),
            fileContentHashes: null,
            existingContentHashes: null,
            excludePatterns: ["*battlemap*"]);

        Assert.Contains(ScanCandidateFlag.LikelyNotRules, result.Single().Flags);
    }

    [Fact]
    public void Analyze_CustomExcludePatterns_ExcludeEverything()
    {
        // A maps folder can flag all files with a "*" pattern.
        var result = Sut.Analyze(
            new[] { File("Players Handbook.pdf") },
            new HashSet<string>(),
            fileContentHashes: null,
            existingContentHashes: null,
            excludePatterns: ["*"]);

        Assert.Contains(ScanCandidateFlag.LikelyNotRules, result.Single().Flags);
    }

    [Fact]
    public void Analyze_CustomExcludePatterns_NonMatchingRulesFileNotFlagged()
    {
        var result = Sut.Analyze(
            new[] { File("Players Handbook.pdf") },
            new HashSet<string>(),
            fileContentHashes: null,
            existingContentHashes: null,
            excludePatterns: ["*token*"]);

        Assert.DoesNotContain(ScanCandidateFlag.LikelyNotRules, result.Single().Flags);
    }
}
