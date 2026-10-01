namespace Aircane.Application.DTOs.AiSettings;

/// <summary>
/// Result of testing an AI provider connection.
/// </summary>
public sealed class TestConnectionResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? ProviderName { get; init; }
    public string? Model { get; init; }
}
