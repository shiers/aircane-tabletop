using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// A deterministic fake AI provider for use in tests and local development
/// when no real AI provider is configured.
/// <para>
/// <see cref="ChatCompletionAsync"/> echoes the last user message back with a
/// fixed prefix so callers can assert on the content.
/// </para>
/// <para>
/// <see cref="StructuredChatCompletionAsync"/> returns a fixed
/// <see cref="AiStructuredOutput"/> whose narration is derived from the last
/// user message, making it predictable in tests.
/// </para>
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    /// <inheritdoc />
    public string ProviderName => "Fake";

    /// <inheritdoc />
    public Task<string> ChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var lastUserContent = LastUserContent(messages);
        var response = $"[Fake AI] {lastUserContent}";
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<AiStructuredOutput> StructuredChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var lastUserContent = LastUserContent(messages);

        var output = new AiStructuredOutput
        {
            Narration = $"[Fake AI] {lastUserContent}",
            PrivateDmNote = null,
            RulesCitations = [],
            ProposedActions = [],
        };

        return Task.FromResult(output);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the content of the last message with role "user", or a fallback
    /// string when no user message is present.
    /// </summary>
    private static string LastUserContent(IReadOnlyList<AiMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == "user")
                return messages[i].Content;
        }

        return "(no user message)";
    }
}
