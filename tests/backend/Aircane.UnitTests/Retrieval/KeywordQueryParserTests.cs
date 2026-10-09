using Aircane.Infrastructure.Retrieval;
using Xunit;

namespace Aircane.UnitTests.Retrieval;

public class KeywordQueryParserTests
{
    [Fact]
    public void ExtractTerms_DropsStopWordsAndQuestionWords()
    {
        Assert.Equal(["actor", "skill"], KeywordQueryParser.ExtractTerms("explain Actor skill"));
    }

    [Fact]
    public void ExtractTerms_SplitsOnPunctuationAndDeduplicates()
    {
        Assert.Equal(
            ["grapple", "attack", "action"],
            KeywordQueryParser.ExtractTerms("How does grapple work? Is grapple an attack-action?"));
    }

    [Fact]
    public void ExtractTerms_DropsShortTokens()
    {
        Assert.Equal(["opportunity", "attacks"], KeywordQueryParser.ExtractTerms("do I get opportunity attacks"));
    }

    [Fact]
    public void ExtractTerms_AllTokensFiltered_FallsBackToWholeQuery()
    {
        Assert.Equal(["how does it work"], KeywordQueryParser.ExtractTerms("  How does it work  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractTerms_BlankQuery_ReturnsEmpty(string query)
    {
        Assert.Empty(KeywordQueryParser.ExtractTerms(query));
    }

    [Fact]
    public void ExtractTerms_CapsTermCount()
    {
        var query = string.Join(' ', Enumerable.Range(0, 20).Select(i => $"term{i}"));
        Assert.Equal(KeywordQueryParser.MaxTerms, KeywordQueryParser.ExtractTerms(query).Count);
    }
}
