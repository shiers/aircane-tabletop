using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// The host's decision about which discovered files to import from a watched folder, with optional
/// per-file overrides of the suggested classification. Only files listed here are considered;
/// files omitted entirely are left un-imported.
/// </summary>
/// <param name="FolderId">The watched folder the selection applies to.</param>
/// <param name="Items">Per-file import decisions.</param>
public sealed record FolderImportSelectionRequest(
    Guid FolderId,
    IReadOnlyList<FolderImportSelectionItem> Items);

/// <summary>
/// A single file's import decision plus optional metadata overrides. When an override is null the
/// folder default (or filename-derived suggestion) is used.
/// </summary>
/// <param name="SourcePath">Absolute path of the discovered file (matches a preview candidate).</param>
/// <param name="Import">True to import this file; false to skip it.</param>
/// <param name="Title">Override title. Null falls back to the filename-derived title.</param>
/// <param name="SourceType">Override source type. Null falls back to the folder default.</param>
/// <param name="GameSystem">Override game system. Null falls back to the folder default.</param>
/// <param name="Ruleset">Override ruleset. Null falls back to the folder default.</param>
public sealed record FolderImportSelectionItem(
    string SourcePath,
    bool Import,
    string? Title = null,
    SourceType? SourceType = null,
    string? GameSystem = null,
    string? Ruleset = null);
