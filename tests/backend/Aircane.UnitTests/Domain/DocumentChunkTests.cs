using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Pgvector;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class DocumentChunkTests
{
    [Fact]
    public void DocumentChunk_Constructor_SetsRequiredProperties()
    {
        var sourceDocId = Guid.NewGuid();
        var chunk = new DocumentChunk(
            sourceDocumentId: sourceDocId,
            chunkIndex: 3,
            text: "A fireball spell deals 8d6 fire damage.");

        Assert.Equal(sourceDocId, chunk.SourceDocumentId);
        Assert.Equal(3, chunk.ChunkIndex);
        Assert.Equal("A fireball spell deals 8d6 fire damage.", chunk.Text);
        Assert.Null(chunk.PageNumber);
        Assert.Null(chunk.SectionTitle);
        Assert.Null(chunk.ChunkType);
        Assert.Equal(ContentVisibility.DMOnly, chunk.Visibility);
        Assert.Equal("{}", chunk.MetadataJson);
        Assert.Null(chunk.Embedding);
    }

    [Fact]
    public void DocumentChunk_Constructor_SetsOptionalProperties()
    {
        var chunk = new DocumentChunk(
            sourceDocumentId: Guid.NewGuid(),
            chunkIndex: 0,
            text: "Chapter 1",
            pageNumber: 5,
            sectionTitle: "Introduction",
            chunkType: "overview");

        Assert.Equal(5, chunk.PageNumber);
        Assert.Equal("Introduction", chunk.SectionTitle);
        Assert.Equal("overview", chunk.ChunkType);
    }

    [Fact]
    public void DocumentChunk_Embedding_CanBeSet()
    {
        var chunk = new DocumentChunk(Guid.NewGuid(), 0, "text");
        var embedding = new Vector(new float[] { 0.1f, 0.2f, 0.3f });
        chunk.Embedding = embedding;
        Assert.Equal(embedding, chunk.Embedding);
    }
}
