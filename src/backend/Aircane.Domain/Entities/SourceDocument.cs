using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// Represents an imported or generated source document (rules PDF, adventure, homebrew, etc.).
/// The file itself is never copied into app storage — this entity holds metadata and import state.
/// For FolderWatch mode, SourcePath is the absolute filesystem path.
/// For Upload mode (future), SourcePath is the managed storage key.
/// </summary>
public class SourceDocument : EntityBase
{
    public string Title { get; set; }
    public string OriginalFileName { get; init; }
    public SourceType SourceType { get; set; }

    /// <summary>
    /// Discriminates how this document was made available: registered folder path or direct upload.
    /// </summary>
    public SourceMode SourceMode { get; set; }

    public string GameSystem { get; set; }
    public string Ruleset { get; set; }

    /// <summary>
    /// Absolute filesystem path (FolderWatch) or managed storage key (Upload).
    /// Replaces the former StoragePath field.
    /// </summary>
    public string SourcePath { get; set; }

    /// <summary>
    /// Foreign key to the WatchedFolder this document was discovered in.
    /// Null when SourceMode is Upload.
    /// </summary>
    public Guid? WatchedFolderId { get; set; }

    /// <summary>
    /// Navigation property to the parent WatchedFolder. Null when SourceMode is Upload.
    /// </summary>
    public WatchedFolder? WatchedFolder { get; set; }

    /// <summary>
    /// False when the file at SourcePath is no longer accessible (moved, deleted, or folder unmounted).
    /// Set to false by the import job or folder scan when the path cannot be opened.
    /// </summary>
    public bool IsSourceAvailable { get; set; }

    public ContentVisibility Visibility { get; set; }
    public ImportStatus ImportStatus { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Ruleset tags for this document (e.g. "D&amp;D 5e 2014", "Pathfinder 2e").
    /// Stored as a JSON array in the database.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    public SourceDocument(
        string title,
        string originalFileName,
        SourceType sourceType,
        SourceMode sourceMode,
        string gameSystem,
        string ruleset,
        string sourcePath,
        Guid? watchedFolderId = null,
        ContentVisibility visibility = ContentVisibility.DMOnly,
        ImportStatus importStatus = ImportStatus.Pending,
        List<string>? tags = null)
    {
        Title = title;
        OriginalFileName = originalFileName;
        SourceType = sourceType;
        SourceMode = sourceMode;
        GameSystem = gameSystem;
        Ruleset = ruleset;
        SourcePath = sourcePath;
        WatchedFolderId = watchedFolderId;
        IsSourceAvailable = true;
        Visibility = visibility;
        ImportStatus = importStatus;
        Tags = tags ?? [];
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private SourceDocument() : base()
    {
        Title = string.Empty;
        OriginalFileName = string.Empty;
        GameSystem = string.Empty;
        Ruleset = string.Empty;
        SourcePath = string.Empty;
    }
}
