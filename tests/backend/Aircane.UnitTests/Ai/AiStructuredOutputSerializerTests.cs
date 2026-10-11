using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.Validation;
using Aircane.Infrastructure.Ai;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// The providers' wire contract is snake_case with numeric enums, but the AI DM prompt documents
/// camelCase keys and action types by name. Both shapes must bind, otherwise proposed actions are
/// silently dropped on real providers.
/// </summary>
public class AiStructuredOutputSerializerTests
{
    [Fact]
    public void Deserialize_CamelCaseKeysAndStringActionType_BindsAction()
    {
        const string json = """
            {
              "narration": "You step into the hall.",
              "privateDmNote": "Skeletons wait in the alcoves.",
              "proposedActions": [
                { "type": "advanceScene", "sceneId": "scene-2", "narration": "On you go." }
              ]
            }
            """;

        var output = AiStructuredOutputSerializer.Deserialize(json)!;

        Assert.Equal("Skeletons wait in the alcoves.", output.PrivateDmNote);
        var action = Assert.Single(output.ProposedActions);
        Assert.Equal(AiActionType.AdvanceScene, action.Type);
        Assert.Equal("scene-2", action.SceneId);
        Assert.Equal("On you go.", action.Narration);
    }

    [Fact]
    public void Deserialize_SnakeCaseKeysAndNumericActionType_StillBindsLegacyShape()
    {
        var json = $$"""
            {
              "narration": "Roll for it.",
              "private_dm_note": "DC 12.",
              "proposed_actions": [
                { "type": {{(int)AiActionType.RequestRoll}}, "formula": "1d20+2", "label": "Stealth", "dc": 12 }
              ]
            }
            """;

        var output = AiStructuredOutputSerializer.Deserialize(json)!;

        Assert.Equal("DC 12.", output.PrivateDmNote);
        var action = Assert.Single(output.ProposedActions);
        Assert.Equal(AiActionType.RequestRoll, action.Type);
        Assert.Equal(12, action.Dc);
    }

    [Theory]
    [InlineData("AdvanceScene")]
    [InlineData("advanceScene")]
    [InlineData("ADVANCESCENE")]
    public void Deserialize_ActionTypeNameIsCaseInsensitive(string typeName)
    {
        var json = $$"""{ "narration": "x", "proposedActions": [ { "type": "{{typeName}}", "sceneId": "scene-2" } ] }""";

        var action = Assert.Single(AiStructuredOutputSerializer.Deserialize(json)!.ProposedActions);

        Assert.Equal(AiActionType.AdvanceScene, action.Type);
    }

    [Fact]
    public void Deserialize_AllAdventureActionFields_Bind()
    {
        const string json = """
            {
              "narration": "x",
              "proposedActions": [
                { "type": "updateNpc", "npcId": "npc-archivist", "hasBeenMet": true, "isAlive": false,
                  "revealedInfoId": "key", "notes": "Trusts you now" },
                { "type": "revealClue", "clueId": "clue-1" },
                { "type": "revealSecret", "secretId": "secret-1" },
                { "type": "completeAdventure", "outcomeNotes": "Done." }
              ]
            }
            """;

        var actions = AiStructuredOutputSerializer.Deserialize(json)!.ProposedActions;

        Assert.Equal("npc-archivist", actions[0].NpcId);
        Assert.True(actions[0].HasBeenMet);
        Assert.False(actions[0].IsAlive);
        Assert.Equal("key", actions[0].RevealedInfoId);
        Assert.Equal("Trusts you now", actions[0].Notes);
        Assert.Equal("clue-1", actions[1].ClueId);
        Assert.Equal("secret-1", actions[2].SecretId);
        Assert.Equal("Done.", actions[3].OutcomeNotes);
    }

    [Fact]
    public void Deserialize_ExistingActionsKeepWorking_AndStringValuesAreNotRewritten()
    {
        // Values (including embedded JSON in a string field) must not be touched by key normalisation.
        var characterId = Guid.NewGuid();
        var json = $$"""
            {
              "narration": "Combat!",
              "proposedActions": [
                { "type": "applyDamage", "characterId": "{{characterId}}", "amount": 7, "reason": "camelCaseValue stays" },
                { "type": "startEncounter", "combatPayloadJson": "{\"someKey\":1}" }
              ]
            }
            """;

        var actions = AiStructuredOutputSerializer.Deserialize(json)!.ProposedActions;

        Assert.Equal(AiActionType.ApplyDamage, actions[0].Type);
        Assert.Equal(characterId, actions[0].CharacterId);
        Assert.Equal(7, actions[0].Amount);
        Assert.Equal("camelCaseValue stays", actions[0].Reason);
        Assert.Equal("{\"someKey\":1}", actions[1].CombatPayloadJson);
    }

    [Fact]
    public void Deserialize_InvalidJson_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => AiStructuredOutputSerializer.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_JsonNull_ReturnsNull()
    {
        Assert.Null(AiStructuredOutputSerializer.Deserialize("null"));
    }

    [Fact]
    public void Deserialize_ThenParseValidator_RejectsAdventureActionMissingItsTarget()
    {
        // End-to-end shape check: what the model emits -> typed action -> validator.
        var output = AiStructuredOutputSerializer.Deserialize(
            """{ "narration": "x", "proposedActions": [ { "type": "revealClue" } ] }""")!;

        var parser = new AiOutputParser(new AiStructuredOutputValidator());
        var result = parser.Parse(JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        }));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("Clue ID is required"));
    }
}
