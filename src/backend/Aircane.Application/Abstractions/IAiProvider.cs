namespace Aircane.Application.Abstractions;

/// <summary>
/// Abstraction over a chat-completion AI model.
/// Implementations include <c>FakeAiProvider</c> (tests/dev), OpenAI, Azure OpenAI,
/// AWS Bedrock, and local Ollama.
/// <para>
/// The AI must never directly mutate campaign state. All state-changing responses
/// must be returned as <see cref="AiStructuredOutput"/> and validated server-side
/// before being applied.
/// </para>
/// </summary>
public interface IAiProvider
{
    /// <summary>Human-readable provider name used for logging and diagnostics (e.g. "Fake", "OpenAI").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Sends a list of chat messages to the AI and returns the raw text response.
    /// Use this for free-form responses such as rules questions where structured
    /// output is not required.
    /// </summary>
    /// <param name="messages">Ordered list of messages forming the conversation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The AI's text response.</returns>
    Task<string> ChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a list of chat messages to the AI and returns a validated
    /// <see cref="AiStructuredOutput"/> response.
    /// Use this for any AI DM response that may include proposed state-changing actions.
    /// </summary>
    /// <param name="messages">Ordered list of messages forming the conversation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A structured output containing narration, optional private DM note,
    /// rules citations, and proposed actions.
    /// </returns>
    Task<AiStructuredOutput> StructuredChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default);
}
