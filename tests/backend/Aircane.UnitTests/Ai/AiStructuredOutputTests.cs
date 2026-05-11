using Aircane.Application.Abstractions;
using System.Text.Json;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="AiStructuredOutput"/> construction and JSON round-trip.
/// </summary>
public class AiStructuredOutputTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void AiStructuredOutput_MinimalConstruction_DefaultsToEmptyCollections()
    {
        var output = new AiStructuredOutput { Narration = "The door creaks open." };

        Assert.Equal("The door creaks open.", output.Narration);
        Assert.Null(output.PrivateDmNote);
        Assert.Empty(output.RulesCitations);
        Assert.Empty(output.ProposedActions);
    }

    [Fact]
    public void AiStructuredOutput_WithPrivateDmNote_StoresNote()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The rogue slips past.",
            PrivateDmNote = "The guard noticed but will not act until next round.",
        };

        Assert.Equal("The guard noticed but will not act until next round.", output.PrivateDmNote);
    }

    [Fact]
    public void AiStructuredOutput_WithCitations_StoresCitations()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var output = new AiStructuredOutput
        {
            Narration = "Per the rules...",
            RulesCitations =
            [
                new AiRulesCitation
                {
                    SourceDocumentId = docId,
                    ChunkId = chunkId,
                    Summary = "Stealth rules, PHB p.177",
                },
            ],
        };

        Assert.Single(output.RulesCitations);
        Assert.Equal(docId, output.RulesCitations[0].SourceDocumentId);
        Assert.Equal(chunkId, output.RulesCitations[0].ChunkId);
        Assert.Equal("Stealth rules, PHB p.177", output.RulesCitations[0].Summary);
    }

    [Fact]
    public void AiStructuredOutput_WithProposedActions_StoresActions()
    {
        var characterId = Guid.NewGuid();

        var output = new AiStructuredOutput
        {
            Narration = "The guards are ahead.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    CharacterId = characterId,
                    Label = "Dexterity (Stealth)",
                    Formula = "1d20+5",
                    Dc = 14,
                    Visibility = "public",
                    Reason = "Sneaking past guards",
                },
            ],
        };

        Assert.Single(output.ProposedActions);
        var action = output.ProposedActions[0];
        Assert.Equal(AiActionType.RequestRoll, action.Type);
        Assert.Equal(characterId, action.CharacterId);
        Assert.Equal("Dexterity (Stealth)", action.Label);
        Assert.Equal("1d20+5", action.Formula);
        Assert.Equal(14, action.Dc);
        Assert.Equal("public", action.Visibility);
        Assert.Equal("Sneaking past guards", action.Reason);
    }

    // ── JSON round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void AiStructuredOutput_JsonRoundTrip_PreservesNarration()
    {
        var original = new AiStructuredOutput
        {
            Narration = "You enter the dungeon.",
            PrivateDmNote = "The trap is armed.",
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AiStructuredOutput>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Narration, deserialized.Narration);
        Assert.Equal(original.PrivateDmNote, deserialized.PrivateDmNote);
    }

    [Fact]
    public void AiStructuredOutput_JsonRoundTrip_PreservesProposedActions()
    {
        var characterId = Guid.NewGuid();
        var original = new AiStructuredOutput
        {
            Narration = "Roll for initiative.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    CharacterId = characterId,
                    Label = "Initiative",
                    Formula = "1d20+2",
                    Dc = null,
                    Visibility = "public",
                    Reason = "Combat starts",
                },
            ],
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AiStructuredOutput>(json);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.ProposedActions);
        var action = deserialized.ProposedActions[0];
        Assert.Equal(AiActionType.RequestRoll, action.Type);
        Assert.Equal(characterId, action.CharacterId);
        Assert.Equal("1d20+2", action.Formula);
    }

    [Fact]
    public void AiStructuredOutput_JsonRoundTrip_PreservesCitations()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var original = new AiStructuredOutput
        {
            Narration = "According to the rules...",
            RulesCitations =
            [
                new AiRulesCitation
                {
                    SourceDocumentId = docId,
                    ChunkId = chunkId,
                    Summary = "Grapple rules",
                },
            ],
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AiStructuredOutput>(json);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.RulesCitations);
        Assert.Equal(docId, deserialized.RulesCitations[0].SourceDocumentId);
        Assert.Equal("Grapple rules", deserialized.RulesCitations[0].Summary);
    }

    // ── AiActionType coverage ─────────────────────────────────────────────────

    [Theory]
    [InlineData(AiActionType.Narrate)]
    [InlineData(AiActionType.RequestRoll)]
    [InlineData(AiActionType.ApplyDamage)]
    [InlineData(AiActionType.ApplyHealing)]
    [InlineData(AiActionType.ApplyCondition)]
    [InlineData(AiActionType.RemoveCondition)]
    [InlineData(AiActionType.RevealContent)]
    [InlineData(AiActionType.MoveScene)]
    [InlineData(AiActionType.CreateNPC)]
    [InlineData(AiActionType.StartEncounter)]
    [InlineData(AiActionType.AdvanceTurn)]
    [InlineData(AiActionType.AwardTreasure)]
    [InlineData(AiActionType.AddQuestFlag)]
    [InlineData(AiActionType.UpdateWorldFlag)]
    [InlineData(AiActionType.AskClarifyingQuestion)]
    public void AiActionType_AllValuesRoundTripThroughJson(AiActionType actionType)
    {
        var action = new AiProposedAction { Type = actionType };
        var json = JsonSerializer.Serialize(action);
        var deserialized = JsonSerializer.Deserialize<AiProposedAction>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(actionType, deserialized.Type);
    }
}
