using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for document classification metadata on <see cref="SourceDocument"/>
/// and the <see cref="UpdateClassificationRequest"/> / <see cref="SourceDocumentDto"/> contracts.
/// </summary>
public class ClassificationUpdateTests
{
    // ── SourceDocument Tags property ──────────────────────────────────────────

    [Fact]
    public void SourceDocument_Tags_DefaultsToEmptyList()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path");

        Assert.NotNull(doc.Tags);
        Assert.Empty(doc.Tags);
    }

    [Fact]
    public void SourceDocument_Tags_CanBeSetViaConstructor()
    {
        var tags = new List<string> { "D&D 5e 2014", "Pathfinder 2e" };
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path", tags: tags);

        Assert.Equal(2, doc.Tags.Count);
        Assert.Contains("D&D 5e 2014", doc.Tags);
        Assert.Contains("Pathfinder 2e", doc.Tags);
    }

    [Fact]
    public void SourceDocument_Tags_CanBeUpdatedDirectly()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.Tags = ["D&D 5e 2014"];

        Assert.Single(doc.Tags);
        Assert.Equal("D&D 5e 2014", doc.Tags[0]);
    }

    [Fact]
    public void SourceDocument_Tags_CanBeCleared()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path",
            tags: ["D&D 5e 2014"]);
        doc.Tags = [];

        Assert.Empty(doc.Tags);
    }

    // ── SourceDocument mutable classification fields ───────────────────────────

    [Fact]
    public void SourceDocument_Title_CanBeUpdated()
    {
        var doc = new SourceDocument("Original", "file.pdf", SourceType.Unknown, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.Title = "Updated Title";

        Assert.Equal("Updated Title", doc.Title);
    }

    [Fact]
    public void SourceDocument_SourceType_CanBeUpdated()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Unknown, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.SourceType = SourceType.Rules;

        Assert.Equal(SourceType.Rules, doc.SourceType);
    }

    [Fact]
    public void SourceDocument_GameSystem_CanBeUpdated()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.GameSystem = "Pathfinder 2e";

        Assert.Equal("Pathfinder 2e", doc.GameSystem);
    }

    [Fact]
    public void SourceDocument_Ruleset_CanBeUpdated()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.Ruleset = "D&D 5e 2014";

        Assert.Equal("D&D 5e 2014", doc.Ruleset);
    }

    // ── Classification update logic (applied to entity) ───────────────────────

    [Theory]
    [InlineData(SourceType.Rules)]
    [InlineData(SourceType.Adventure)]
    [InlineData(SourceType.Solo)]
    [InlineData(SourceType.Character)]
    [InlineData(SourceType.Homebrew)]
    [InlineData(SourceType.Generated)]
    [InlineData(SourceType.Unknown)]
    public void SourceDocument_SupportsAllSourceTypeValues(SourceType sourceType)
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Unknown, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.SourceType = sourceType;

        Assert.Equal(sourceType, doc.SourceType);
    }

    [Fact]
    public void ApplyClassificationUpdate_OnlyTitle_LeavesOtherFieldsUnchanged()
    {
        var doc = new SourceDocument("Original", "file.pdf", SourceType.Adventure, SourceMode.Upload, "D&D 5e", "2014", "/path",
            tags: ["D&D 5e 2014"]);

        // Simulate what UpdateClassificationAsync does for a title-only request
        var request = new UpdateClassificationRequest(Title: "New Title");
        ApplyClassification(doc, request);

        Assert.Equal("New Title", doc.Title);
        Assert.Equal(SourceType.Adventure, doc.SourceType);
        Assert.Equal("D&D 5e", doc.GameSystem);
        Assert.Equal("2014", doc.Ruleset);
        Assert.Single(doc.Tags);
    }

    [Fact]
    public void ApplyClassificationUpdate_AllFields_UpdatesAll()
    {
        var doc = new SourceDocument("Original", "file.pdf", SourceType.Unknown, SourceMode.Upload, "D&D 5e", "2014", "/path");

        var request = new UpdateClassificationRequest(
            Title: "Complete Update",
            SourceType: SourceType.Solo,
            GameSystem: "Pathfinder 2e",
            Ruleset: "Pathfinder 2e Remaster",
            Tags: ["Pathfinder 2e", "Remaster"]);
        ApplyClassification(doc, request);

        Assert.Equal("Complete Update", doc.Title);
        Assert.Equal(SourceType.Solo, doc.SourceType);
        Assert.Equal("Pathfinder 2e", doc.GameSystem);
        Assert.Equal("Pathfinder 2e Remaster", doc.Ruleset);
        Assert.Equal(2, doc.Tags.Count);
    }

    [Fact]
    public void ApplyClassificationUpdate_NullRequest_ChangesNothing()
    {
        var doc = new SourceDocument("Original", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path",
            tags: ["D&D 5e 2014"]);

        var request = new UpdateClassificationRequest(); // all nulls
        ApplyClassification(doc, request);

        Assert.Equal("Original", doc.Title);
        Assert.Equal(SourceType.Rules, doc.SourceType);
        Assert.Equal("D&D 5e", doc.GameSystem);
        Assert.Equal("2014", doc.Ruleset);
        Assert.Single(doc.Tags);
    }

    [Fact]
    public void ApplyClassificationUpdate_EmptyTags_ClearsTags()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path",
            tags: ["D&D 5e 2014"]);

        var request = new UpdateClassificationRequest(Tags: []);
        ApplyClassification(doc, request);

        Assert.Empty(doc.Tags);
    }

    // ── SourceDocumentDto Tags field ──────────────────────────────────────────

    [Fact]
    public void SourceDocumentDto_IncludesTags()
    {
        var dto = new SourceDocumentDto(
            Id: Guid.NewGuid(),
            Title: "Test",
            OriginalFileName: "test.pdf",
            SourceType: SourceType.Rules,
            SourceMode: SourceMode.Upload,
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            Visibility: ContentVisibility.DMOnly,
            ImportStatus: ImportStatus.Pending,
            IsSourceAvailable: true,
            WatchedFolderId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Tags: ["D&D 5e 2014"]);

        Assert.Single(dto.Tags);
        Assert.Equal("D&D 5e 2014", dto.Tags[0]);
    }

    [Fact]
    public void SourceDocumentDto_EmptyTags_IsAllowed()
    {
        var dto = new SourceDocumentDto(
            Id: Guid.NewGuid(),
            Title: "Test",
            OriginalFileName: "test.pdf",
            SourceType: SourceType.Unknown,
            SourceMode: SourceMode.Upload,
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            Visibility: ContentVisibility.DMOnly,
            ImportStatus: ImportStatus.Pending,
            IsSourceAvailable: true,
            WatchedFolderId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Tags: []);

        Assert.Empty(dto.Tags);
    }

    // ── UpdateClassificationRequest ───────────────────────────────────────────

    [Fact]
    public void UpdateClassificationRequest_AllFieldsOptional_DefaultsToNull()
    {
        var request = new UpdateClassificationRequest();

        Assert.Null(request.Title);
        Assert.Null(request.SourceType);
        Assert.Null(request.GameSystem);
        Assert.Null(request.Ruleset);
        Assert.Null(request.Tags);
    }

    [Fact]
    public void UpdateClassificationRequest_CanSetAllFields()
    {
        var request = new UpdateClassificationRequest(
            Title: "My Title",
            SourceType: SourceType.Homebrew,
            GameSystem: "D&D 5e",
            Ruleset: "D&D 5e 2014",
            Tags: ["D&D 5e 2014"]);

        Assert.Equal("My Title", request.Title);
        Assert.Equal(SourceType.Homebrew, request.SourceType);
        Assert.Equal("D&D 5e", request.GameSystem);
        Assert.Equal("D&D 5e 2014", request.Ruleset);
        Assert.Single(request.Tags!);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the patch logic in <see cref="Aircane.Infrastructure.Library.LibraryService.UpdateClassificationAsync"/>.
    /// Kept here so tests validate the same conditional-update semantics.
    /// </summary>
    private static void ApplyClassification(SourceDocument doc, UpdateClassificationRequest request)
    {
        if (request.Title is not null)
            doc.Title = request.Title;

        if (request.SourceType.HasValue)
            doc.SourceType = request.SourceType.Value;

        if (request.GameSystem is not null)
            doc.GameSystem = request.GameSystem;

        if (request.Ruleset is not null)
            doc.Ruleset = request.Ruleset;

        if (request.Tags is not null)
            doc.Tags = [.. request.Tags];
    }
}
