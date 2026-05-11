namespace Aircane.Application.DTOs.GameSystems;

/// <summary>
/// Request body for creating a new Game System Definition.
/// </summary>
public sealed record CreateGameSystemRequest(
    string DefinitionJson);

/// <summary>
/// Request body for updating an existing Game System Definition.
/// </summary>
public sealed record UpdateGameSystemRequest(
    string DefinitionJson);

/// <summary>
/// Request body for validating a Game System Definition without saving.
/// </summary>
public sealed record ValidateGameSystemRequest(
    string DefinitionJson);
