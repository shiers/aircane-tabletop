namespace Aircane.Application.DocumentProcessing;

/// <summary>
/// Text content extracted from a single page of a PDF document.
/// </summary>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="Text">The extracted text content for this page.</param>
/// <param name="CharacterCount">Number of characters in the extracted text.</param>
public record PageText(
    int PageNumber,
    string Text,
    int CharacterCount);
