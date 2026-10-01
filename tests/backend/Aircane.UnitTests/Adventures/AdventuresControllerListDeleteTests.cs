using Aircane.Api.Controllers;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class AdventuresControllerListDeleteTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventuresController _controller;

    public AdventuresControllerListDeleteTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);

        // Create controller with minimal dependencies (only dbContext and logger needed for list/delete)
        _controller = new AdventuresController(
            validator: null!,
            generationService: null!,
            retrievalService: null!,
            adventureIndexingService: null!,
            partyAnalysisService: null!,
            dbContext: _db,
            logger: NullLogger<AdventuresController>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GeneratedAdventure CreateAdventure(
        string title,
        GeneratedAdventureStatus status,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        return new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = status,
            RequestJson = "{}",
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow,
        };
    }

    private static SourceDocument CreateSourceDocumentForAdventure(Guid adventureId)
    {
        return new SourceDocument(
            title: "Generated Adventure",
            originalFileName: $"generated-adventure-{adventureId}.json",
            sourceType: SourceType.Generated,
            sourceMode: SourceMode.Upload,
            gameSystem: "D&D 5e 2014",
            ruleset: "D&D 5e 2014",
            sourcePath: $"generated://{adventureId}",
            visibility: ContentVisibility.DMOnly,
            importStatus: ImportStatus.Completed,
            tags: ["generated", "adventure"]);
    }

    private static DocumentChunk CreateChunk(Guid sourceDocumentId, int index)
    {
        return new DocumentChunk(
            sourceDocumentId: sourceDocumentId,
            chunkIndex: index,
            text: $"Chunk {index} text content",
            sectionTitle: $"Section {index}",
            chunkType: "scene",
            visibility: ContentVisibility.Public,
            metadataJson: "{}");
    }

    // ── GET /api/adventures Tests ─────────────────────────────────────────────

    [Fact]
    public async Task ListAdventures_returns_empty_array_when_no_adventures_exist()
    {
        var result = await _controller.ListAdventures(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value);
        Assert.Empty(adventures);
    }

    [Fact]
    public async Task ListAdventures_returns_all_adventures_without_filter()
    {
        _db.GeneratedAdventures.AddRange(
            CreateAdventure("Draft Adventure", GeneratedAdventureStatus.Draft),
            CreateAdventure("Approved Adventure", GeneratedAdventureStatus.Approved),
            CreateAdventure("Active Adventure", GeneratedAdventureStatus.Active));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAdventures(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value).ToList();
        Assert.Equal(3, adventures.Count);
    }

    [Fact]
    public async Task ListAdventures_filters_by_draft_status()
    {
        _db.GeneratedAdventures.AddRange(
            CreateAdventure("Draft 1", GeneratedAdventureStatus.Draft),
            CreateAdventure("Draft 2", GeneratedAdventureStatus.Draft),
            CreateAdventure("Approved", GeneratedAdventureStatus.Approved));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAdventures(GeneratedAdventureStatus.Draft, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value).ToList();
        Assert.Equal(2, adventures.Count);
        Assert.All(adventures, a => Assert.Equal("Draft", a.Status));
    }

    [Fact]
    public async Task ListAdventures_filters_by_approved_status()
    {
        _db.GeneratedAdventures.AddRange(
            CreateAdventure("Draft", GeneratedAdventureStatus.Draft),
            CreateAdventure("Approved", GeneratedAdventureStatus.Approved));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAdventures(GeneratedAdventureStatus.Approved, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value).ToList();
        Assert.Single(adventures);
        Assert.Equal("Approved", adventures[0].Status);
    }

    [Fact]
    public async Task ListAdventures_filters_by_active_status()
    {
        _db.GeneratedAdventures.AddRange(
            CreateAdventure("Active", GeneratedAdventureStatus.Active),
            CreateAdventure("Draft", GeneratedAdventureStatus.Draft));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAdventures(GeneratedAdventureStatus.Active, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value).ToList();
        Assert.Single(adventures);
        Assert.Equal("Active", adventures[0].Status);
    }

    [Fact]
    public async Task ListAdventures_returns_correct_dto_fields()
    {
        var adventure = CreateAdventure("Test Adventure", GeneratedAdventureStatus.Draft,
            createdAt: new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            updatedAt: new DateTime(2024, 1, 16, 12, 0, 0, DateTimeKind.Utc));
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();

        var result = await _controller.ListAdventures(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value).ToList();
        var dto = Assert.Single(adventures);
        Assert.Equal(adventure.Id, dto.Id);
        Assert.Equal("Test Adventure", dto.Title);
        Assert.Equal("Draft", dto.Status);
        Assert.Equal("D&D 5e 2014", dto.Ruleset);
        Assert.Equal("D&D 5e 2014", dto.GameSystem);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc), dto.CreatedAt);
        Assert.Equal(new DateTime(2024, 1, 16, 12, 0, 0, DateTimeKind.Utc), dto.UpdatedAt);
    }

    [Fact]
    public async Task ListAdventures_orders_by_updated_at_descending()
    {
        _db.GeneratedAdventures.AddRange(
            CreateAdventure("Oldest", GeneratedAdventureStatus.Draft,
                updatedAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateAdventure("Newest", GeneratedAdventureStatus.Draft,
                updatedAt: new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateAdventure("Middle", GeneratedAdventureStatus.Draft,
                updatedAt: new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAdventures(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var adventures = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(okResult.Value).ToList();
        Assert.Equal("Newest", adventures[0].Title);
        Assert.Equal("Middle", adventures[1].Title);
        Assert.Equal("Oldest", adventures[2].Title);
    }

    // ── DELETE /api/adventures/{id} Tests ─────────────────────────────────────

    [Fact]
    public async Task DeleteAdventure_returns_not_found_when_adventure_does_not_exist()
    {
        var result = await _controller.DeleteAdventure(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteAdventure_removes_adventure_entity()
    {
        var adventure = CreateAdventure("To Delete", GeneratedAdventureStatus.Draft);
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteAdventure(adventure.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.GeneratedAdventures.FindAsync(adventure.Id));
    }

    [Fact]
    public async Task DeleteAdventure_removes_source_document_and_chunks()
    {
        var adventure = CreateAdventure("Indexed Adventure", GeneratedAdventureStatus.Approved);
        _db.GeneratedAdventures.Add(adventure);

        var sourceDoc = CreateSourceDocumentForAdventure(adventure.Id);
        _db.SourceDocuments.Add(sourceDoc);
        await _db.SaveChangesAsync();

        var chunk1 = CreateChunk(sourceDoc.Id, 0);
        var chunk2 = CreateChunk(sourceDoc.Id, 1);
        var chunk3 = CreateChunk(sourceDoc.Id, 2);
        _db.DocumentChunks.AddRange(chunk1, chunk2, chunk3);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteAdventure(adventure.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.GeneratedAdventures.FindAsync(adventure.Id));
        Assert.Null(await _db.SourceDocuments.FindAsync(sourceDoc.Id));
        Assert.Empty(await _db.DocumentChunks.Where(c => c.SourceDocumentId == sourceDoc.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteAdventure_succeeds_when_no_indexed_chunks_exist()
    {
        // Adventure that was never indexed (still in Draft, no SourceDocument)
        var adventure = CreateAdventure("Unindexed", GeneratedAdventureStatus.Draft);
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteAdventure(adventure.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.GeneratedAdventures.FindAsync(adventure.Id));
    }

    [Fact]
    public async Task DeleteAdventure_does_not_affect_other_adventures()
    {
        var adventure1 = CreateAdventure("Keep This", GeneratedAdventureStatus.Approved);
        var adventure2 = CreateAdventure("Delete This", GeneratedAdventureStatus.Draft);
        _db.GeneratedAdventures.AddRange(adventure1, adventure2);
        await _db.SaveChangesAsync();

        await _controller.DeleteAdventure(adventure2.Id, CancellationToken.None);

        Assert.NotNull(await _db.GeneratedAdventures.FindAsync(adventure1.Id));
        Assert.Null(await _db.GeneratedAdventures.FindAsync(adventure2.Id));
    }
}
