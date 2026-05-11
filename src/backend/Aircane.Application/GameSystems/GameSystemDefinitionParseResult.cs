using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Result of parsing a Game System Definition JSON document.
/// </summary>
public record GameSystemDefinitionParseResult
{
    public GameSystemDefinition? Definition { get; init; }
    public IReadOnlyList<GameSystemDefinitionParseError> Errors { get; init; } = [];
    public bool IsSuccess => Definition is not null && Errors.Count == 0;

    public static GameSystemDefinitionParseResult Success(GameSystemDefinition definition) =>
        new() { Definition = definition };

    public static GameSystemDefinitionParseResult Failure(params GameSystemDefinitionParseError[] errors) =>
        new() { Errors = errors };

    public static GameSystemDefinitionParseResult Failure(IReadOnlyList<GameSystemDefinitionParseError> errors) =>
        new() { Errors = errors };
}

/// <summary>
/// A single error encountered during Game System Definition parsing.
/// </summary>
public record GameSystemDefinitionParseError
{
    public string Message { get; init; } = string.Empty;
    public string? FieldPath { get; init; }
    public int? LineNumber { get; init; }
}
