namespace Aircane.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Message to import a single source document: text extraction, chunking, OCR-needed detection,
/// embedding generation, and indexing. Handled by <c>DocumentImportJobHandler</c>.
/// </summary>
public sealed record DocumentImportJobMessage(Guid TargetDocumentId) : IBackgroundJob
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public string JobType => "DocumentImport";
    public Guid? DocumentId => TargetDocumentId;
}

/// <summary>
/// Message to scan a watched folder for new/changed files and import them. Handled by
/// <c>FolderScanJobHandler</c>.
/// </summary>
public sealed record FolderScanJobMessage(Guid FolderId) : IBackgroundJob
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public string JobType => "FolderScan";
    public Guid? DocumentId => null;
}

/// <summary>
/// Message to regenerate embeddings. When <see cref="TargetDocumentId"/> is set, only that
/// document is re-embedded; otherwise the entire library is re-embedded. Handled by
/// <c>ReembedJobHandler</c>.
/// </summary>
public sealed record ReembedJobMessage(Guid? TargetDocumentId) : IBackgroundJob
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public string JobType => TargetDocumentId is null ? "ReembedAll" : "Reembed";
    public Guid? DocumentId => TargetDocumentId;
}

/// <summary>
/// Message to re-run OCR/import on documents currently marked <c>OcrRequired</c>. When
/// <see cref="TargetDocumentId"/> is set, only that document is re-processed; otherwise every
/// OCR-required document is re-processed. Handled by <c>ReocrJobHandler</c>.
/// </summary>
public sealed record ReocrJobMessage(Guid? TargetDocumentId) : IBackgroundJob
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public string JobType => TargetDocumentId is null ? "ReocrAll" : "Reocr";
    public Guid? DocumentId => TargetDocumentId;
}
