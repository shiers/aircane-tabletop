namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Turns a natural-language question into the search terms used by keyword retrieval.
/// A question like "explain the Actor feat" yields ["actor", "feat"]: the text is lower-cased,
/// split on anything that isn't a letter or digit, and stripped of stopwords and very short
/// tokens that would match almost every chunk.
/// </summary>
public static class KeywordQueryParser
{
    /// <summary>Upper bound on terms per query, keeping the generated SQL small.</summary>
    public const int MaxTerms = 8;

    private const int MinTermLength = 3;

    /// <summary>
    /// Common English words plus question/instruction words typical of rules lookups
    /// ("explain", "how", "rule") that carry no meaning for matching rulebook text.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "your", "with", "from", "this", "that",
        "these", "those", "into", "onto", "over", "under", "about", "than", "then", "there",
        "their", "them", "they", "its", "it's", "was", "were", "been", "being", "have", "has",
        "had", "will", "would", "should", "could", "can", "may", "might", "must", "shall", "any",
        "all", "some", "each", "other", "more", "most", "such", "only", "also", "just", "very",
        "what", "when", "where", "which", "who", "whom", "whose", "why", "how", "does", "did",
        "doing", "done", "use", "used", "using", "work", "works", "working", "happen", "happens",
        "explain", "describe", "tell", "show", "give", "list", "define", "definition", "mean",
        "means", "meaning", "rule", "rules", "please", "need", "know", "get", "gets", "let",
    };

    /// <summary>
    /// Extracts distinct search terms from <paramref name="query"/>, in order of appearance.
    /// If every token is filtered out (e.g. "how does it work"), falls back to the trimmed,
    /// lower-cased query as a single phrase term so the search still has something to match.
    /// </summary>
    public static IReadOnlyList<string> ExtractTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var lowered = query.Trim().ToLowerInvariant();

        var terms = SplitTokens(lowered)
            .Where(t => t.Length >= MinTermLength && !StopWords.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTerms)
            .ToList();

        return terms.Count > 0 ? terms : [lowered];
    }

    private static IEnumerable<string> SplitTokens(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\'');
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                yield return text[start..i].Trim('\'');
                start = -1;
            }
        }
    }
}
