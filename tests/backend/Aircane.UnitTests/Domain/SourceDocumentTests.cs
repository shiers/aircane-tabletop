using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class SourceDocumentTests
{
    [Fact]
    public void SourceDocument_Constructor_SetsAllProperties()
    {
        var doc = new SourceDocument(
            title: "Player's Handbook",
            originalFileName: "phb.pdf",
            sourceType: SourceType.Rules,
            sourceMode: SourceMode.Upload,
            gameSystem: "D&D 5e",
            ruleset: "2014",
            sourcePath: "/uploads/phb.pdf");

        Assert.Equal("Player's Handbook", doc.Title);
        Assert.Equal("phb.pdf", doc.OriginalFileName);
        Assert.Equal(SourceType.Rules, doc.SourceType);
        Assert.Equal(SourceMode.Upload, doc.SourceMode);
        Assert.Equal("D&D 5e", doc.GameSystem);
        Assert.Equal("2014", doc.Ruleset);
        Assert.Equal("/uploads/phb.pdf", doc.SourcePath);
        Assert.Equal(ContentVisibility.DMOnly, doc.Visibility);
        Assert.Equal(ImportStatus.Pending, doc.ImportStatus);
        Assert.True(doc.IsSourceAvailable);
    }

    [Fact]
    public void SourceDocument_ImportStatus_CanBeUpdated()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Adventure, SourceMode.Upload, "D&D 5e", "2014", "/path");
        doc.ImportStatus = ImportStatus.Completed;
        Assert.Equal(ImportStatus.Completed, doc.ImportStatus);
    }

    [Fact]
    public void SourceDocument_UpdatedAt_InitiallyEqualsCreatedAt()
    {
        var doc = new SourceDocument("Title", "file.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", "/path");
        Assert.Equal(doc.CreatedAt, doc.UpdatedAt);
    }
}
