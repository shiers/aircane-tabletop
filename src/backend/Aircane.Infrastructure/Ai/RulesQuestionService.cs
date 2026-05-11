using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Answers rules questions using RAG-grounded retrieval and AI generation.
/// Retrieves relevant rules chunks, builds a prompt with context, and calls the AI provider.
/// If no relevant sources are found, the AI is instructed to say it cannot confirm from available sources.
/// </summary>
public sealed class RulesQuestionService : IRulesQuestionService
{
    private readonly IRagContextBuilder _ragContextBuilder;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<RulesQuestionService> _logger;

    public RulesQuestionService(
        IRagContextBuilder ragContextBuilder,
        IAiProvider aiProvider,
        ILogger<RulesQuestionService> logger)
    {
        _ragContextBuilder = ragContextBuilder;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RulesQuestionResponse> AskAsync(
        RulesQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        _logger.LogDebug(
            "Processing rules question: {Question} (GameSystem={GameSystem}, Ruleset={Ruleset})",
            request.Question, request.GameSystem, request.Ruleset);

        // Retrieve relevant rules context
        var ragRequest = new RagContextRequest(
            Query: request.Question,
            GameSystem: request.GameSystem,
            Ruleset: request.Ruleset,
            RequesterRole: ParticipantRole.Host, // Rules questions use full visibility
            TopK: 20,
            MaxContextChars: 8000,
            MinRelevanceScore: 0.3); // Discard low-confidence chunks to reduce hallucination

        var ragResult = await _ragContextBuilder.BuildContextAsync(ragRequest, cancellationToken);

        var hasSourceSupport = ragResult.ChunksIncluded > 0;

        // Build the prompt messages
        var messages = BuildPromptMessages(request.Question, ragResult, hasSourceSupport);

        // Call the AI provider
        var aiResponse = await _aiProvider.ChatCompletionAsync(messages, cancellationToken);

        _logger.LogDebug(
            "Rules question answered with {CitationCount} citations, hasSourceSupport={HasSourceSupport}",
            ragResult.Citations.Count, hasSourceSupport);

        // Map citations from RAG result
        var citations = ragResult.Citations
            .Select(c => new RulesQuestionCitation(
                SourceTitle: c.SourceDocumentTitle,
                PageNumber: c.PageNumber,
                SectionTitle: c.SectionTitle,
                ChunkId: c.ChunkId))
            .ToList();

        return new RulesQuestionResponse(
            Answer: aiResponse,
            Citations: citations,
            HasSourceSupport: hasSourceSupport);
    }

    /// <summary>
    /// Builds the prompt messages for the AI provider, including system instructions,
    /// retrieved context, and the user's question.
    /// </summary>
    public static IReadOnlyList<AiMessage> BuildPromptMessages(
        string question,
        RagContextResult ragResult,
        bool hasSourceSupport)
    {
        var messages = new List<AiMessage>();

        // System message with instructions
        var systemPrompt = BuildSystemPrompt(hasSourceSupport);
        messages.Add(AiMessage.System(systemPrompt));

        // If we have context, include it as a system message
        if (hasSourceSupport && !string.IsNullOrWhiteSpace(ragResult.ContextText))
        {
            messages.Add(AiMessage.System(ragResult.ContextText));
        }

        // User question
        messages.Add(AiMessage.User(question));

        return messages;
    }

    /// <summary>
    /// Builds the system prompt that instructs the AI on how to answer rules questions.
    /// </summary>
    public static string BuildSystemPrompt(bool hasSourceSupport)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a tabletop RPG rules assistant. Your role is to answer rules questions accurately based on the provided source material.");
        sb.AppendLine();

        if (hasSourceSupport)
        {
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- Answer the question using ONLY the retrieved rules context provided below.");
            sb.AppendLine("- Cite specific sources using [N] notation matching the citation numbers in the context.");
            sb.AppendLine("- Distinguish between official rules, house rules, and your own interpretation.");
            sb.AppendLine("- If the provided sources partially answer the question, state what is confirmed and what is uncertain.");
            sb.AppendLine("- Keep answers concise and focused on the rules question asked.");
            sb.AppendLine("- Do not reveal hidden adventure content or DM-only information to players.");
        }
        else
        {
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- No relevant source documents were found for this question, or retrieved sources had insufficient confidence.");
            sb.AppendLine("- You MUST clearly state that you cannot confirm the answer from available sources.");
            sb.AppendLine("- Do NOT present any information as if it were verified by the user's library.");
            sb.AppendLine("- You may provide general knowledge about the rule if you have it, but clearly mark it as unverified and potentially inaccurate.");
            sb.AppendLine("- Preface your response with a clear disclaimer that this answer is not grounded in the user's indexed documents.");
            sb.AppendLine("- Suggest that the user import the relevant rulebook for authoritative answers.");
            sb.AppendLine("- If you are unsure about the rule, say so explicitly rather than guessing.");
        }

        return sb.ToString();
    }
}
