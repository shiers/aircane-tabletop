using Aircane.Infrastructure.DocumentProcessing;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

public class OcrTextCleanupTests
{
    [Theory]
    [InlineData("ActorSkilled at mimicry", "Actor Skilled at mimicry")]
    [InlineData("10 feet.ActorSkilled", "10 feet. Actor Skilled")]
    [InlineData("Already spaced Text stays", "Already spaced Text stays")]
    [InlineData("", "")]
    public void CleanOcrText_InsertsMissingSpaces(string input, string expected)
    {
        Assert.Equal(expected, PdfPigTextExtractor.CleanOcrText(input));
    }
}
