using System.Text.Json;
using System.Security.Cryptography;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// Processes a document import job: reads the stored file, extracts text,
/// chunks it into <see cref="DocumentChunk"/> records, generates embeddings,
/// and updates <see cref="ImportStatus"/>.
/// </summary>
/// <remarks>
/// Background job scheduling (Hangfire/Quartz) will be wired in a later task.
/// This service can be called directly for now.
/// </remarks>
public sealed class DocumentImportJob : IDocumentImportJob
{
    private readonly AircaneDbContext _db;
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly ITextChunker _textChunker;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILibraryHubNotifier _hubNotifier;
    private readonly IDocumentSource _documentSource;
    private readonly IEmbeddingCompatibility? _embeddingCompatibility;
    private readonly ILogger<DocumentImportJob> _logger;

    public DocumentImportJob(
        AircaneDbContext db,
        IPdfTextExtractor pdfExtractor,
        ITextChunker textChunker,
        IEmbeddingProvider embeddingProvider,
        ILibraryHubNotifier hubNotifier,
        IDocumentSource documentSource,
        ILogger<DocumentImportJob> logger,
        IEmbeddingCompatibility? embeddingCompatibility = null)
    {
        _db = db;
        _pdfExtractor = pdfExtractor;
        _textChunker = textChunker;
        _embeddingProvider = embeddingProvider;
        _hubNotifier = hubNotifier;
        _documentSource = documentSource;
        _embeddingCompatibility = embeddingCompatibility;
        _logger = logger;
    }

    /// <inheritdoc />
    // The background job id driving the current import, threaded into ImportStatusUpdated events
    // so the frontend can correlate progress to a specific job. Null for direct/legacy calls.
    private Guid? _currentJobId;

    public async Task ProcessDocumentAsync(Guid documentId, Guid? jobId = null, CancellationToken ct = default)
    {
        _currentJobId = jobId;

        var document = await _db.SourceDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            _logger.LogWarning("DocumentImportJob: document {DocumentId} not found.", documentId);
            return;
        }

        _logger.LogInformation(
            "Starting import for document {DocumentId} '{Title}'.", documentId, document.Title);

        // ── Check source availability before processing ────────────────────────
        var fileExists = await _documentSource.FileExistsAsync(document.SourcePath, ct);
        if (!fileExists)
        {
            _logger.LogWarning(
                "Source file not accessible for document {DocumentId} '{Title}'. Path={SourcePath}. " +
                "Marking as unavailable.",
                documentId, document.Title, document.SourcePath);

            document.IsSourceAvailable = false;
            document.ImportStatus = ImportStatus.Failed;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _hubNotifier.NotifyImportStatusUpdatedAsync(
                new ImportStatusDto(document.Id, document.ImportStatus, null,
                    $"Source file is not accessible: {document.SourcePath}", document.UpdatedAt,
                    document.IsSourceAvailable, _currentJobId),
                CancellationToken.None);

            return;
        }

        document.ImportStatus = ImportStatus.Processing;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        // Compute (or backfill) the content hash for content-based duplicate detection.
        await ComputeContentHashAsync(document, ct);

        await _db.SaveChangesAsync(ct);

        await _hubNotifier.NotifyImportStatusUpdatedAsync(
            new ImportStatusDto(document.Id, document.ImportStatus, null, null, document.UpdatedAt,
                document.IsSourceAvailable, _currentJobId),
            CancellationToken.None);

        try
        {
            await ProcessDocumentContentAsync(document, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Import failed for document {DocumentId} '{Title}'.", documentId, document.Title);

            document.ImportStatus = ImportStatus.Failed;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            await _hubNotifier.NotifyImportStatusUpdatedAsync(
                new ImportStatusDto(document.Id, document.ImportStatus, null, ex.Message, document.UpdatedAt,
                    document.IsSourceAvailable, _currentJobId),
                CancellationToken.None);
        }
    }

    private async Task ProcessDocumentContentAsync(SourceDocument document, CancellationToken ct)
    {
        var extension = Path.GetExtension(document.SourcePath).ToLowerInvariant();

        if (extension == ".json")
        {
            // JSON files don't require text extraction in this task.
            document.ImportStatus = ImportStatus.Completed;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _hubNotifier.NotifyImportStatusUpdatedAsync(
                new ImportStatusDto(document.Id, document.ImportStatus, 100, null, document.UpdatedAt,
                    document.IsSourceAvailable, _currentJobId),
                CancellationToken.None);

            _logger.LogInformation(
                "Document {DocumentId} is JSON; marked as Completed without text extraction.",
                document.Id);
            return;
        }

        if (extension == ".pdf")
        {
            await ProcessPdfAsync(document, ct);
            return;
        }

        // Unknown extension - mark as failed.
        _logger.LogWarning(
            "Document {DocumentId} has unsupported extension '{Extension}'. Marking as Failed.",
            document.Id, extension);

        document.ImportStatus = ImportStatus.Failed;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _hubNotifier.NotifyImportStatusUpdatedAsync(
            new ImportStatusDto(document.Id, document.ImportStatus, null, $"Unsupported file extension: {extension}", document.UpdatedAt,
                document.IsSourceAvailable, _currentJobId),
            CancellationToken.None);
    }

    private async Task ProcessPdfAsync(SourceDocument document, CancellationToken ct)
    {
        await using var stream = await _documentSource.OpenStreamAsync(document.SourcePath, ct);

        var result = await _pdfExtractor.ExtractTextAsync(stream, ct);

        if (result.IsOcrRequired)
        {
            _logger.LogInformation(
                "Document {DocumentId} requires OCR (total chars: {TotalChars}). Marking as OcrRequired.",
                document.Id, result.TotalCharacters);

            document.ImportStatus = ImportStatus.OcrRequired;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _hubNotifier.NotifyImportStatusUpdatedAsync(
                new ImportStatusDto(document.Id, document.ImportStatus, null, null, document.UpdatedAt,
                    document.IsSourceAvailable, _currentJobId),
                CancellationToken.None);
            return;
        }

        // Chunk each page using the sliding-window chunker and collect all chunks.
        var chunks = new List<DocumentChunk>();
        int nextChunkIndex = 0;

        foreach (var page in result.Pages)
        {
            var textChunks = _textChunker.Chunk(
                text: page.Text,
                pageNumber: page.PageNumber,
                startingChunkIndex: nextChunkIndex);

            foreach (var tc in textChunks)
            {
                var metadata = JsonSerializer.Serialize(new
                {
                    sourceDocumentId = document.Id,
                    pageNumber = tc.PageNumber,
                    chunkIndex = tc.ChunkIndex,
                    sourceTitle = document.Title
                });

                chunks.Add(new DocumentChunk(
                    sourceDocumentId: document.Id,
                    chunkIndex: tc.ChunkIndex,
                    text: tc.Text,
                    pageNumber: tc.PageNumber,
                    sectionTitle: tc.SectionTitle,
                    chunkType: "page",
                    visibility: document.Visibility,
                    metadataJson: metadata));

                nextChunkIndex = tc.ChunkIndex + 1;
            }
        }

        // Generate embeddings for all chunks. Failures are non-fatal: we log a warning
        // and leave Embedding null so the document is still marked Completed and
        // keyword/metadata search continues to work.
        await GenerateEmbeddingsAsync(chunks, ct);

        _db.DocumentChunks.AddRange(chunks);

        document.ImportStatus = ImportStatus.Completed;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        var embeddedCount = chunks.Count(c => c.Embedding is not null);
        _logger.LogInformation(
            "Document {DocumentId} imported successfully. Pages: {PageCount}, Chunks: {ChunkCount}, " +
            "EmbeddedChunks: {EmbeddedCount}, TotalChars: {TotalChars}.",
            document.Id, result.Pages.Count, chunks.Count, embeddedCount, result.TotalCharacters);

        await _hubNotifier.NotifyImportStatusUpdatedAsync(
            new ImportStatusDto(document.Id, document.ImportStatus, 100, null, document.UpdatedAt,
                document.IsSourceAvailable, _currentJobId),
            CancellationToken.None);
    }

    /// <summary>
    /// Computes the SHA-256 content hash of the document's file bytes and stores it on the entity.
    /// Best-effort: a hashing failure is logged and does not fail the import (the document is
    /// simply left without a content hash).
    /// </summary>
    private async Task ComputeContentHashAsync(SourceDocument document, CancellationToken ct)
    {
        try
        {
            await using var stream = await _documentSource.OpenStreamAsync(document.SourcePath, ct);
            using var sha = SHA256.Create();
            var hashBytes = await sha.ComputeHashAsync(stream, ct);
            document.ContentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to compute content hash for document {DocumentId}; continuing without it.",
                document.Id);
        }
    }

    private async Task GenerateEmbeddingsAsync(List<DocumentChunk> chunks, CancellationToken ct)
    {
        if (chunks.Count == 0)
            return;

        // Skip embedding entirely when the provider dimension doesn't match the pgvector column;
        // writing incompatible vectors would fail. Chunks are still stored (keyword-searchable).
        if (_embeddingCompatibility is { IsVectorSearchEnabled: false })
        {
            _logger.LogWarning(
                "Skipping embedding generation: provider dimension ({ProviderDim}) does not match the " +
                "column ({ColumnDim}). Chunks stored without embeddings; migrate the column and re-embed.",
                _embeddingCompatibility.ProviderDimension, _embeddingCompatibility.ColumnDimension);
            return;
        }

        try
        {
            var texts = chunks.Select(c => c.Text).ToList();
            var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(texts, ct);

            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Embedding = new Vector(embeddings[i]);
                chunks[i].EmbeddingProvider = _embeddingProvider.ProviderName;
                chunks[i].EmbeddingModel = _embeddingProvider.ModelName;
                chunks[i].EmbeddingDimensions = embeddings[i].Length;
            }

            _logger.LogDebug(
                "Generated {Count} embeddings using provider '{Provider}'.",
                chunks.Count, _embeddingProvider.ProviderName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Embedding generation failed using provider '{Provider}'. " +
                "Chunks will be stored without embeddings; vector search will not be available for this document.",
                _embeddingProvider.ProviderName);
        }
    }
}
