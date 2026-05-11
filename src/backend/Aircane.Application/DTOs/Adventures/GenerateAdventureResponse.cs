namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Response returned when an adventure generation job is accepted.
/// </summary>
public sealed record GenerateAdventureResponse(
    Guid JobId,
    string Status,
    string Message);
