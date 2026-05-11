namespace Aircane.Application.Abstractions;

/// <summary>
/// Represents a single message in a chat conversation sent to an AI provider.
/// </summary>
/// <param name="Role">The role of the message author: <c>system</c>, <c>user</c>, or <c>assistant</c>.</param>
/// <param name="Content">The text content of the message.</param>
public sealed record AiMessage(string Role, string Content)
{
    /// <summary>Creates a system-role message (instructions/context for the AI).</summary>
    public static AiMessage System(string content) => new("system", content);

    /// <summary>Creates a user-role message (player or DM input).</summary>
    public static AiMessage User(string content) => new("user", content);

    /// <summary>Creates an assistant-role message (prior AI response, for multi-turn context).</summary>
    public static AiMessage Assistant(string content) => new("assistant", content);
}
