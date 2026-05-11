using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Request to register a new watched folder.
/// </summary>
public sealed record RegisterFolderRequest(
    string DisplayName,
    string AbsolutePath,
    SourceType DefaultSourceType,
    string? DefaultGameSystem = null,
    string? DefaultRuleset = null);
