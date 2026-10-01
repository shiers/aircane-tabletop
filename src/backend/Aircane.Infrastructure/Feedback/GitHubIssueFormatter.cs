using System.Globalization;
using System.Text;
using Aircane.Application.Feedback;

namespace Aircane.Infrastructure.Feedback;

/// <summary>
/// Builds the GitHub issue title and markdown body from a <see cref="FeedbackDto"/>. Pure and
/// static so it is unit-testable without any HTTP. Null diagnostic fields render as a dash so
/// the issue is always well-formed and never contains the literal text "null".
/// </summary>
public static class GitHubIssueFormatter
{
    private const string Dash = "-";

    /// <summary>Builds the issue title: <c>[Beta] {summary}</c>.</summary>
    public static string BuildTitle(string summary) => $"[Beta] {summary}";

    /// <summary>
    /// Builds the full markdown issue body. <paramref name="summary"/> and
    /// <paramref name="description"/> should already be sanitized by the caller.
    /// </summary>
    public static string BuildBody(FeedbackDto dto, string description)
    {
        var diag = dto.DiagnosticContext;
        var submittedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();

        sb.AppendLine("## Description");
        sb.AppendLine(string.IsNullOrWhiteSpace(description) ? Dash : description);
        sb.AppendLine();

        sb.AppendLine("## Steps to reproduce");
        sb.AppendLine(Blank(dto.StepsToReproduce));
        sb.AppendLine();

        sb.AppendLine("## Expected behaviour");
        sb.AppendLine(Blank(dto.ExpectedBehaviour));
        sb.AppendLine();

        sb.AppendLine("## Diagnostic context");
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine($"| Submitted at | {submittedAt} |");
        sb.AppendLine($"| Platform | {Cell(diag?.Platform)} |");
        sb.AppendLine($"| App version | {Cell(diag?.AppVersion)} |");
        sb.AppendLine($"| AI provider | {ProviderCell(diag?.AiProvider, diag?.AiModel)} |");
        sb.AppendLine($"| Embedding provider | {ProviderCell(diag?.EmbeddingProvider, diag?.EmbeddingModel)} |");
        sb.AppendLine($"| Active route | {Cell(diag?.ActiveRoute)} |");
        sb.AppendLine($"| Session ID | {Cell(diag?.SessionId)} |");
        sb.AppendLine($"| Campaign ID | {Cell(diag?.CampaignId)} |");
        sb.AppendLine($"| User role | {Cell(diag?.UserRole)} |");
        sb.AppendLine();

        sb.AppendLine("## Recent session events (last 10)");
        sb.AppendLine("```");
        sb.AppendLine(JoinLines(dto.RecentEvents));
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Console errors (last 5)");
        sb.AppendLine("```");
        sb.AppendLine(JoinLines(dto.ConsoleErrors));
        sb.AppendLine("```");
        sb.AppendLine();

        sb.Append("*Submitted via Aircane Tabletop in-app feedback. Beta build.*");

        return sb.ToString();
    }

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Dash : value;

    private static string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Dash : value.Replace("|", "\\|");

    private static string ProviderCell(string? provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(model))
            return Dash;

        var p = string.IsNullOrWhiteSpace(provider) ? Dash : provider;
        var m = string.IsNullOrWhiteSpace(model) ? Dash : model;
        return $"{p} ({m})".Replace("|", "\\|");
    }

    private static string JoinLines(IReadOnlyList<string>? lines)
    {
        if (lines is null || lines.Count == 0)
            return "(none)";

        return string.Join("\n", lines);
    }
}
