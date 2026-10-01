using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.Validation;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="AiOutputParser"/> covering JSON deserialization,
/// markdown fence stripping, validation integration, and graceful error handling.
/// </summary>
public class AiOutputParserTests
{
    private readonly AiOutputParser _parser;

    public AiOutputParserTests()
    {
        _parser = new AiOutputParser(new AiStructuredOutputValidator());
    }

    // ── Successful parsing ────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidMinimalJson_ReturnsSuccess()
    {
        var json = """
        {
            "narration": "The door creaks open.",
            "privateDmNote": null,
            "rulesCitations": [],
            "proposedActions": []
        }
        """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Output);
        Assert.Equal("The door creaks open.", result.Output.Narration);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_ValidFullJson_ReturnsSuccess()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var charId = Guid.NewGuid();

        var json = $$"""
        {
            "narration": "You enter the chamber.",
            "privateDmNote": "The trap is armed.",
            "rulesCitations": [
                {
                    "sourceDocumentId": "{{docId}}",
                    "chunkId": "{{chunkId}}",
                    "summary": "Trap rules"
                }
            ],
            "proposedActions": [
                {
                    "type": "RequestRoll",
                    "characterId": "{{charId}}",
                    "label": "Dexterity Saving Throw",
                    "formula": "1d20+3",
                    "dc": 15,
                    "visibility": "public",
                    "reason": "Avoiding the trap"
                }
            ]
        }
        """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Output);
        Assert.Equal("You enter the chamber.", result.Output.Narration);
        Assert.Equal("The trap is armed.", result.Output.PrivateDmNote);
        Assert.Single(result.Output.RulesCitations);
        Assert.Equal(docId, result.Output.RulesCitations[0].SourceDocumentId);
        Assert.Single(result.Output.ProposedActions);
        Assert.Equal(AiActionType.RequestRoll, result.Output.ProposedActions[0].Type);
        Assert.Equal(charId, result.Output.ProposedActions[0].CharacterId);
    }

    [Fact]
    public void Parse_JsonWithMultipleActions_ReturnsSuccess()
    {
        var charId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();

        var json = $$"""
        {
            "narration": "Combat begins!",
            "proposedActions": [
                {
                    "type": "RequestRoll",
                    "characterId": "{{charId}}",
                    "label": "Initiative",
                    "formula": "1d20+2"
                },
                {
                    "type": "ApplyDamage",
                    "characterId": "{{charId}}",
                    "amount": 12,
                    "reason": "Goblin attack"
                },
                {
                    "type": "MoveScene",
                    "targetSceneId": "{{sceneId}}",
                    "reason": "Moving to combat arena"
                }
            ]
        }
        """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Output);
        Assert.Equal(3, result.Output.ProposedActions.Count);
    }

    // ── Markdown fence stripping ──────────────────────────────────────────────

    [Fact]
    public void Parse_JsonWrappedInMarkdownFence_ReturnsSuccess()
    {
        var json = """
        ```json
        {
            "narration": "The door opens.",
            "rulesCitations": [],
            "proposedActions": []
        }
        ```
        """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("The door opens.", result.Output!.Narration);
    }

    [Fact]
    public void Parse_JsonWrappedInPlainMarkdownFence_ReturnsSuccess()
    {
        var json = """
        ```
        {
            "narration": "Hello world.",
            "rulesCitations": [],
            "proposedActions": []
        }
        ```
        """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello world.", result.Output!.Narration);
    }

    // ── Malformed JSON handling ───────────────────────────────────────────────

    [Fact]
    public void Parse_NullInput_ReturnsFailure()
    {
        var result = _parser.Parse(null);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Output);
        Assert.Single(result.Errors);
        Assert.Contains("null or empty", result.Errors[0]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsFailure()
    {
        var result = _parser.Parse("");

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Contains("null or empty", result.Errors[0]);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsFailure()
    {
        var result = _parser.Parse("   \n\t  ");

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsFailureWithMessage()
    {
        var result = _parser.Parse("{ this is not valid json }");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Output);
        Assert.Single(result.Errors);
        Assert.Contains("Failed to parse AI output as JSON", result.Errors[0]);
    }

    [Fact]
    public void Parse_TruncatedJson_ReturnsFailure()
    {
        var json = """{ "narration": "Hello""";

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to parse AI output as JSON", result.Errors[0]);
    }

    [Fact]
    public void Parse_JsonArray_ReturnsFailure()
    {
        var json = """[{"narration": "Hello."}]""";

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
    }

    // ── Validation failures ───────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidJsonButEmptyNarration_ReturnsValidationError()
    {
        var json = """
        {
            "narration": "",
            "rulesCitations": [],
            "proposedActions": []
        }
        """;

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Output);
        Assert.Contains(result.Errors, e => e.Contains("Narration"));
    }

    [Fact]
    public void Parse_ValidJsonButInvalidAction_ReturnsValidationErrors()
    {
        var json = """
        {
            "narration": "Roll for stealth.",
            "proposedActions": [
                {
                    "type": "RequestRoll",
                    "label": null,
                    "formula": null
                }
            ]
        }
        """;

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count > 0);
    }

    [Fact]
    public void Parse_ValidJsonButCitationWithEmptyGuid_ReturnsValidationError()
    {
        var json = """
        {
            "narration": "Per the rules...",
            "rulesCitations": [
                {
                    "sourceDocumentId": "00000000-0000-0000-0000-000000000000",
                    "chunkId": "00000000-0000-0000-0000-000000000000",
                    "summary": "Some rule"
                }
            ],
            "proposedActions": []
        }
        """;

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("UUID"));
    }

    // ── Case insensitivity ────────────────────────────────────────────────────

    [Fact]
    public void Parse_CamelCasePropertyNames_ReturnsSuccess()
    {
        var json = """
        {
            "Narration": "Hello.",
            "RulesCitations": [],
            "ProposedActions": []
        }
        """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello.", result.Output!.Narration);
    }

    // ── RawJson preservation ──────────────────────────────────────────────────

    [Fact]
    public void Parse_PreservesRawJsonOnSuccess()
    {
        var json = """{"narration": "Test.", "rulesCitations": [], "proposedActions": []}""";

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(json, result.RawJson);
    }

    [Fact]
    public void Parse_PreservesRawJsonOnFailure()
    {
        var json = "not json at all";

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal(json, result.RawJson);
    }

    // ── Async parsing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ValidJson_ReturnsSuccess()
    {
        var json = """
        {
            "narration": "Async test.",
            "rulesCitations": [],
            "proposedActions": []
        }
        """;

        var result = await _parser.ParseAsync(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("Async test.", result.Output!.Narration);
    }

    [Fact]
    public async Task ParseAsync_InvalidJson_ReturnsFailure()
    {
        var result = await _parser.ParseAsync("broken json {{{");

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to parse AI output as JSON", result.Errors[0]);
    }
}
