using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Validation;
using Aircane.Api.Extensions;
using Aircane.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Manages watched folder registration: the host-defined folder paths from which
/// source documents are discovered and indexed.
/// Source files on disk are never modified or deleted by this controller.
/// </summary>
[ApiController]
[Route("api/library/folders")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public sealed class LibraryFoldersController : ControllerBase
{
    private readonly ILibraryService _libraryService;
    private readonly IFolderScanJob _folderScanJob;
    private readonly IValidator<RegisterFolderRequest> _registerValidator;
    private readonly IValidator<UpdateFolderRequest> _updateValidator;

    public LibraryFoldersController(
        ILibraryService libraryService,
        IFolderScanJob folderScanJob,
        IValidator<RegisterFolderRequest> registerValidator,
        IValidator<UpdateFolderRequest> updateValidator)
    {
        _libraryService = libraryService;
        _folderScanJob = folderScanJob;
        _registerValidator = registerValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Registers a new watched folder.
    /// </summary>
    /// <remarks>
    /// The folder path must be an absolute path accessible to the server process.
    /// Source files are never copied - the app reads them in place.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(WatchedFolderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterFolder(
        [FromBody] RegisterFolderRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new RegisterFolderRequest(
            DisplayName: body.DisplayName,
            AbsolutePath: body.AbsolutePath,
            DefaultSourceType: body.DefaultSourceType,
            DefaultGameSystem: body.DefaultGameSystem,
            DefaultRuleset: body.DefaultRuleset);

        var validation = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToValidationProblemDetails());

        var dto = await _libraryService.RegisterFolderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetFolder), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Lists all registered watched folders.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WatchedFolderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFolders(CancellationToken cancellationToken)
    {
        var folders = await _libraryService.ListFoldersAsync(cancellationToken);
        return Ok(folders);
    }

    /// <summary>
    /// Returns a single watched folder by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WatchedFolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolder(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _libraryService.GetFolderAsync(id, cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Updates the settings of an existing watched folder.
    /// Only the fields provided in the request body are applied; omitted fields are left unchanged.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WatchedFolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFolder(
        Guid id,
        [FromBody] UpdateFolderRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new UpdateFolderRequest(
            DisplayName: body.DisplayName,
            AbsolutePath: body.AbsolutePath,
            DefaultSourceType: body.DefaultSourceType,
            DefaultGameSystem: body.DefaultGameSystem,
            DefaultRuleset: body.DefaultRuleset);

        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToValidationProblemDetails());

        try
        {
            var dto = await _libraryService.UpdateFolderAsync(id, request, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Unregisters a watched folder and removes all associated SourceDocument records and
    /// their indexed chunks from the database.
    /// Source files on disk are never deleted.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _libraryService.DeleteFolderAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Triggers a manual rescan of the specified watched folder.
    /// New files are indexed and import jobs are enqueued.
    /// Changed files have their import status reset and are re-imported.
    /// Unchanged files are skipped.
    /// </summary>
    /// <remarks>
    /// The scan runs synchronously within the request. For large folders this may take some time.
    /// The response includes a summary of files found, new, updated, and skipped.
    /// </remarks>
    [HttpPost("{id:guid}/scan")]
    [ProducesResponseType(typeof(FolderScanResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ScanFolder(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _folderScanJob.ScanFolderAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Browses the server filesystem and returns subdirectories of the given path.
    /// Used by the frontend folder picker to let the host navigate to a folder
    /// without manually typing the full path.
    /// </summary>
    /// <remarks>
    /// Only directories are returned (no files). System directories are excluded.
    /// If no path is provided, returns filesystem roots (drive letters on Windows, "/" on Linux/macOS).
    /// </remarks>
    [HttpGet("browse")]
    [ProducesResponseType(typeof(BrowseFoldersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult BrowseFolders([FromQuery] string? path)
    {
        // If no path provided, return filesystem roots
        if (string.IsNullOrWhiteSpace(path))
        {
            var roots = Directory.GetLogicalDrives()
                .Select(d => new BrowseDirectoryEntry(
                    Name: d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    FullPath: d))
                .ToList();

            return Ok(new BrowseFoldersResponse(
                CurrentPath: null,
                ParentPath: null,
                Directories: roots));
        }

        // Validate the path
        if (!FolderPathValidator.IsAbsolutePath(path))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid path",
                Detail = "Path must be absolute.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (FolderPathValidator.ContainsTraversalSequence(path))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid path",
                Detail = "Path must not contain traversal sequences (..).",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (!Directory.Exists(path))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Path not found",
                Detail = "The specified directory does not exist or is not accessible.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var canonicalPath = Path.GetFullPath(path);
            var parentPath = Directory.GetParent(canonicalPath)?.FullName;

            var directories = Directory.GetDirectories(canonicalPath)
                .Select(d => new DirectoryInfo(d))
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Where(d => !FolderPathValidator.IsSystemDirectory(d.FullName))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new BrowseDirectoryEntry(Name: d.Name, FullPath: d.FullName))
                .ToList();

            return Ok(new BrowseFoldersResponse(
                CurrentPath: canonicalPath,
                ParentPath: parentPath,
                Directories: directories));
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Access denied",
                Detail = "The server process does not have permission to read this directory.",
                Status = StatusCodes.Status400BadRequest,
            });
        }
        catch (IOException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "IO error",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }
}

/// <summary>
/// Response model for the folder browse endpoint.
/// </summary>
public sealed record BrowseFoldersResponse(
    string? CurrentPath,
    string? ParentPath,
    IReadOnlyList<BrowseDirectoryEntry> Directories);

/// <summary>
/// A single directory entry returned by the browse endpoint.
/// </summary>
public sealed record BrowseDirectoryEntry(string Name, string FullPath);

/// <summary>
/// Request body for POST /api/library/folders.
/// </summary>
public sealed class RegisterFolderRequestBody
{
    /// <summary>Human-readable name for this folder (e.g. "D&amp;D 5e Rules").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Absolute filesystem path to the folder (e.g. "/home/user/rpg/rules").</summary>
    public string AbsolutePath { get; set; } = string.Empty;

    /// <summary>Default source type applied to documents discovered in this folder.</summary>
    public SourceType DefaultSourceType { get; set; } = SourceType.Rules;

    /// <summary>Optional default game system tag (e.g. "D&amp;D 5e").</summary>
    public string? DefaultGameSystem { get; set; }

    /// <summary>Optional default ruleset tag (e.g. "2014").</summary>
    public string? DefaultRuleset { get; set; }
}

/// <summary>
/// Request body for PUT /api/library/folders/{id}.
/// All fields are optional; only provided fields are updated.
/// </summary>
public sealed class UpdateFolderRequestBody
{
    /// <summary>New display name for the folder.</summary>
    public string? DisplayName { get; set; }

    /// <summary>New absolute filesystem path.</summary>
    public string? AbsolutePath { get; set; }

    /// <summary>New default source type.</summary>
    public SourceType? DefaultSourceType { get; set; }

    /// <summary>New default game system tag.</summary>
    public string? DefaultGameSystem { get; set; }

    /// <summary>New default ruleset tag.</summary>
    public string? DefaultRuleset { get; set; }
}
