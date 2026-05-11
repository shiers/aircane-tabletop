using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Validation;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Manages the source document library: upload, list, metadata retrieval, and deletion.
/// </summary>
/// <remarks>
/// <para>
/// SECURITY DESIGN DECISION: Source documents (PDFs, rulebooks, adventures) are NEVER served
/// directly to clients through this controller or any other endpoint. The API returns only
/// metadata (title, type, status, etc.) — never file content or filesystem paths.
/// </para>
/// <para>
/// Source files are read server-side by the import pipeline for text extraction, chunking,
/// and embedding generation. The derived data (chunks, embeddings) powers RAG retrieval,
/// but the original files remain private to the host's filesystem.
/// </para>
/// <para>
/// There is intentionally NO file download endpoint. If a future "export/share" feature is
/// needed, it must be implemented as an explicit host action with its own authorization,
/// not as a default capability.
/// </para>
/// <para>
/// See also: <c>SourceFileAccessBlockerMiddleware</c> which blocks direct HTTP requests
/// for source document file extensions as a defense-in-depth measure.
/// </para>
/// </remarks>
[ApiController]
[Route("api/library/documents")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public sealed class LibraryController : ControllerBase
{
    private readonly ILibraryService _libraryService;
    private readonly ILogger<LibraryController> _logger;
    private readonly IConfiguration _configuration;

    public LibraryController(ILibraryService libraryService, ILogger<LibraryController> logger, IConfiguration configuration)
    {
        _libraryService = libraryService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Uploads a source document (PDF or JSON) and creates a library record.
    /// Accepts multipart/form-data with the file and metadata fields.
    /// </summary>
    /// <remarks>
    /// Allowed file types: .pdf, .json (max 50 MB).
    /// The file is stored on the server filesystem; it is never served from a public static path.
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SourceDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(52_428_800)] // 50 MB + a small overhead for form fields
    public async Task<IActionResult> UploadDocument(
        [FromForm] UploadDocumentFormModel form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new ProblemDetails
            {
                Title = "No file provided",
                Detail = "A file must be included in the request.",
                Status = StatusCodes.Status400BadRequest,
            });

        // --- Upload safety checks ---

        // Validate file size against configurable limit.
        var maxUploadSize = _configuration.GetValue<long?>("Library:MaxUploadSizeBytes")
            ?? FileUploadValidator.DefaultMaxUploadSizeBytes;

        if (!FileUploadValidator.ValidateFileSize(form.File.Length, maxUploadSize))
            return BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = $"File size ({form.File.Length} bytes) exceeds the maximum allowed size ({maxUploadSize} bytes).",
                Status = StatusCodes.Status400BadRequest,
            });

        // Validate file extension.
        if (!FileUploadValidator.IsAllowedExtension(form.File.FileName))
            return BadRequest(new ProblemDetails
            {
                Title = "File type not allowed",
                Detail = $"Only the following file extensions are allowed: {string.Join(", ", FileUploadValidator.GetAllowedExtensions())}.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Validate MIME type.
        if (!FileUploadValidator.IsAllowedMimeType(form.File.ContentType))
            return BadRequest(new ProblemDetails
            {
                Title = "Content type not allowed",
                Detail = $"Only the following MIME types are allowed: {string.Join(", ", FileUploadValidator.GetAllowedMimeTypes())}.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Sanitize the filename.
        var sanitizedFileName = FileUploadValidator.SanitizeFileName(form.File.FileName);

        var request = new UploadDocumentRequest(
            Title: form.Title,
            OriginalFileName: sanitizedFileName,
            SourceType: form.SourceType,
            GameSystem: form.GameSystem,
            Ruleset: form.Ruleset,
            Visibility: form.Visibility,
            FileContent: form.File.OpenReadStream(),
            ContentType: form.File.ContentType);

        try
        {
            var dto = await _libraryService.UploadDocumentAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetDocument), new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Document upload rejected: {Message}", ex.Message);
            return BadRequest(new ProblemDetails
            {
                Title = "Upload rejected",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Lists source documents with optional filters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SourceDocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(
        [FromQuery] SourceType? sourceType,
        [FromQuery] string? gameSystem,
        [FromQuery] string? ruleset,
        [FromQuery] ImportStatus? importStatus,
        [FromQuery] bool? isSourceAvailable,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var request = new DocumentListRequest(
            SourceType: sourceType,
            GameSystem: gameSystem,
            Ruleset: ruleset,
            ImportStatus: importStatus,
            IsSourceAvailable: isSourceAvailable,
            Page: page,
            PageSize: pageSize);

        var documents = await _libraryService.ListDocumentsAsync(request, cancellationToken);
        return Ok(documents);
    }

    /// <summary>
    /// Returns metadata for a single document. Does not return file content or filesystem paths.
    /// </summary>
    /// <remarks>
    /// This endpoint intentionally returns only the <see cref="SourceDocumentDto"/> which excludes
    /// the <c>SourcePath</c> property from the domain entity. There is no companion endpoint to
    /// download or stream the source file — this is by design to protect private source documents.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SourceDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _libraryService.GetDocumentAsync(id, cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Returns the current import job status, progress percentage, and any error message
    /// for the specified document.
    /// </summary>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(ImportStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImportStatus(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _libraryService.GetImportStatusAsync(id, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Deletes a document record. File cleanup is attempted on a best-effort basis.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _libraryService.DeleteDocumentAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Updates the classification metadata of an existing document.
    /// Only the fields provided in the request body are applied; omitted fields are left unchanged.
    /// </summary>
    /// <remarks>
    /// Updatable fields: title, sourceType, gameSystem, ruleset, tags.
    /// </remarks>
    [HttpPatch("{id:guid}/classification")]
    [ProducesResponseType(typeof(SourceDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateClassification(
        Guid id,
        [FromBody] UpdateClassificationBody body,
        CancellationToken cancellationToken)
    {
        var request = new UpdateClassificationRequest(
            Title: body.Title,
            SourceType: body.SourceType,
            GameSystem: body.GameSystem,
            Ruleset: body.Ruleset,
            Tags: body.Tags);

        try
        {
            var dto = await _libraryService.UpdateClassificationAsync(id, request, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

/// <summary>
/// Form model for the document upload endpoint.
/// Bound from multipart/form-data.
/// </summary>
public sealed class UploadDocumentFormModel
{
    /// <summary>Display title for the document.</summary>
    [FromForm(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>The uploaded file (PDF or JSON).</summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    /// <summary>Classification of the document content.</summary>
    [FromForm(Name = "sourceType")]
    public SourceType SourceType { get; set; } = SourceType.Unknown;

    /// <summary>Game system this document belongs to (e.g. "D&amp;D 5e").</summary>
    [FromForm(Name = "gameSystem")]
    public string GameSystem { get; set; } = string.Empty;

    /// <summary>Ruleset version (e.g. "2014").</summary>
    [FromForm(Name = "ruleset")]
    public string Ruleset { get; set; } = string.Empty;

    /// <summary>Visibility of the document to session participants.</summary>
    [FromForm(Name = "visibility")]
    public ContentVisibility Visibility { get; set; } = ContentVisibility.DMOnly;
}

/// <summary>
/// Request body for the PATCH classification endpoint.
/// All fields are optional; only provided fields are updated.
/// </summary>
public sealed class UpdateClassificationBody
{
    /// <summary>New display title for the document.</summary>
    public string? Title { get; set; }

    /// <summary>New source type classification.</summary>
    public SourceType? SourceType { get; set; }

    /// <summary>New game system (e.g. "D&amp;D 5e").</summary>
    public string? GameSystem { get; set; }

    /// <summary>New ruleset version (e.g. "D&amp;D 5e 2014").</summary>
    public string? Ruleset { get; set; }

    /// <summary>Ruleset tags (e.g. ["D&amp;D 5e 2014", "Pathfinder 2e"]).</summary>
    public IReadOnlyList<string>? Tags { get; set; }
}
