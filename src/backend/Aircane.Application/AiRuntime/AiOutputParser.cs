using System.Text.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;
using FluentValidation;
using FluentValidation.Results;

namespace Aircane.Application.AiRuntime;

/// <summary>
/// Result of parsing AI output. Contains either a valid structured output
/// or a list of validation errors explaining why the output was rejected.
/// </summary>
public sealed record AiOutputParseResult
{
    /// <summary>The successfully parsed and validated output. Null when parsing or validation failed.</summary>
    public AiStructuredOutput? Output { get; init; }

    /// <summary>True when parsing and validation both succeeded.</summary>
    public bool IsSuccess => Output is not null && Errors.Count == 0;

    /// <summary>Errors encountered during parsing or validation.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The raw JSON that was attempted to be parsed. Useful for diagnostics.</summary>
    public string? RawJson { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static AiOutputParseResult Success(AiStructuredOutput output, string rawJson) =>
        new() { Output = output, Errors = [], RawJson = rawJson };

    /// <summary>Creates a failed result with the given errors.</summary>
    public static AiOutputParseResult Failure(IReadOnlyList<string> errors, string? rawJson = null) =>
        new() { Output = null, Errors = errors, RawJson = rawJson };
}

/// <summary>
/// Parses raw AI JSON output into a validated <see cref="AiStructuredOutput"/>.
/// Handles malformed JSON gracefully by returning validation errors rather than throwing.
/// </summary>
public sealed class AiOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IValidator<AiStructuredOutput> _validator;

    public AiOutputParser(IValidator<AiStructuredOutput> validator)
    {
        _validator = validator;
    }

    /// <summary>
    /// Attempts to parse and validate raw JSON from the AI provider.
    /// Returns a result containing either the validated output or descriptive errors.
    /// </summary>
    /// <param name="rawJson">The raw JSON string returned by the AI model.</param>
    /// <returns>A parse result indicating success or failure with error details.</returns>
    public AiOutputParseResult Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return AiOutputParseResult.Failure(
                ["AI output is null or empty."],
                rawJson);
        }

        // Attempt to extract JSON from markdown code fences if present
        var cleanedJson = ExtractJsonFromMarkdown(rawJson);

        AiStructuredOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<AiStructuredOutput>(cleanedJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AiOutputParseResult.Failure(
                [$"Failed to parse AI output as JSON: {ex.Message}"],
                rawJson);
        }

        if (output is null)
        {
            return AiOutputParseResult.Failure(
                ["AI output deserialized to null."],
                rawJson);
        }

        // Validate the deserialized output
        ValidationResult validationResult = _validator.Validate(output);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => e.ErrorMessage)
                .ToList();

            return AiOutputParseResult.Failure(errors, rawJson);
        }

        return AiOutputParseResult.Success(output, rawJson);
    }

    /// <summary>
    /// Attempts to parse and validate raw JSON from the AI provider asynchronously.
    /// </summary>
    /// <param name="rawJson">The raw JSON string returned by the AI model.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A parse result indicating success or failure with error details.</returns>
    public async Task<AiOutputParseResult> ParseAsync(string? rawJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return AiOutputParseResult.Failure(
                ["AI output is null or empty."],
                rawJson);
        }

        var cleanedJson = ExtractJsonFromMarkdown(rawJson);

        AiStructuredOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<AiStructuredOutput>(cleanedJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AiOutputParseResult.Failure(
                [$"Failed to parse AI output as JSON: {ex.Message}"],
                rawJson);
        }

        if (output is null)
        {
            return AiOutputParseResult.Failure(
                ["AI output deserialized to null."],
                rawJson);
        }

        var validationResult = await _validator.ValidateAsync(output, ct);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => e.ErrorMessage)
                .ToList();

            return AiOutputParseResult.Failure(errors, rawJson);
        }

        return AiOutputParseResult.Success(output, rawJson);
    }

    /// <summary>
    /// Strips markdown code fences (```json ... ```) that some AI models wrap around JSON output.
    /// </summary>
    private static string ExtractJsonFromMarkdown(string input)
    {
        var trimmed = input.Trim();

        // Handle ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed[..^3].TrimEnd();
            }
        }

        return trimmed;
    }
}
