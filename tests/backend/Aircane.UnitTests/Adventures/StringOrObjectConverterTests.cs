using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Adventures;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public class StringOrObjectConverterTests
{
    private static string Scene(string dmNotes, string objectives = "[\"Find the tome\"]") => $$"""
        [{ "sceneId": "scene-1", "title": "T", "description": "D", "sceneType": "roleplay",
           "connectsTo": [], "objectives": {{objectives}}, "dmNotes": {{dmNotes}} }]
        """;

    private static GeneratedScene Parse(string json)
        => AdventureGenerationService.ParseJsonResponse<List<GeneratedScene>>(json, "scenes").Single();

    [Fact]
    public void DmNotes_String_IsUsedAsIs()
        => Assert.Equal("Plain notes.", Parse(Scene("\"Plain notes.\"")).DmNotes);

    [Fact]
    public void DmNotes_Object_BecomesCompactJsonString()
    {
        var scene = Parse(Scene("""{ "setup": "Gates", "trap": { "dc": 12 } }"""));

        Assert.Equal("""{ "setup": "Gates", "trap": { "dc": 12 } }""", scene.DmNotes);
    }

    [Fact]
    public void DmNotes_ArrayAndNull_Handled()
    {
        Assert.Equal("""["a","b"]""", Parse(Scene("""["a","b"]""")).DmNotes);
        Assert.Null(Parse(Scene("null")).DmNotes);
    }

    [Fact]
    public void Objectives_ObjectItemsAndLoneString_Tolerated()
    {
        Assert.Equal(["Find it", """{"goal":"x"}"""],
            Parse(Scene("\"n\"", """["Find it", {"goal":"x"}]""")).Objectives);
        Assert.Equal(["Just one"], Parse(Scene("\"n\"", "\"Just one\"")).Objectives);
    }

    [Fact]
    public void Npc_PersonalityAsObject_DoesNotThrow()
    {
        var npc = AdventureGenerationService.ParseJsonResponse<List<GeneratedNpc>>(
            """[{ "name":"A","role":"ally","personality":{"trait":"shy"},"statsSummary":"Commoner","sceneIds":["scene-1"] }]""",
            "npcs").Single();

        Assert.Equal("""{"trait":"shy"}""", npc.Personality);
    }
}
