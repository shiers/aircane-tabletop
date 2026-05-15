using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// Represents a host-registered folder path from which source documents are discovered and indexed.
/// The app never copies files from this folder - it reads them in place and stores only derived data.
/// </summary>
public class WatchedFolder : EntityBase
{
    public string DisplayName { get; set; }
    public string AbsolutePath { get; set; }

    /// <summary>
    /// Default source type applied to documents discovered in this folder.
    /// Does not include Generated or Unknown - those are not valid defaults for a watched folder.
    /// </summary>
    public SourceType DefaultSourceType { get; set; }

    /// <summary>
    /// Optional default game system tag inherited by documents discovered in this folder.
    /// </summary>
    public string? DefaultGameSystem { get; set; }

    /// <summary>
    /// Optional default ruleset tag inherited by documents discovered in this folder.
    /// </summary>
    public string? DefaultRuleset { get; set; }

    /// <summary>
    /// Timestamp of the most recent completed folder scan. Null if the folder has never been scanned.
    /// </summary>
    public DateTimeOffset? LastScannedAt { get; set; }

    /// <summary>
    /// Navigation property: source documents discovered in this folder.
    /// </summary>
    public ICollection<SourceDocument> SourceDocuments { get; set; } = [];

    public WatchedFolder(
        string displayName,
        string absolutePath,
        SourceType defaultSourceType,
        string? defaultGameSystem = null,
        string? defaultRuleset = null)
    {
        DisplayName = displayName;
        AbsolutePath = absolutePath;
        DefaultSourceType = defaultSourceType;
        DefaultGameSystem = defaultGameSystem;
        DefaultRuleset = defaultRuleset;
    }

    // EF Core constructor
    private WatchedFolder() : base()
    {
        DisplayName = string.Empty;
        AbsolutePath = string.Empty;
    }
}
