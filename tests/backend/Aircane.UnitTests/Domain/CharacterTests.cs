using Aircane.Domain.Entities;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class CharacterTests
{
    [Fact]
    public void Character_Constructor_SetsAllProperties()
    {
        var campaignId = Guid.NewGuid();
        var character = new Character(
            name: "Gandalf",
            gameSystem: "D&D 5e",
            ruleset: "2014",
            level: 20,
            canonicalJson: "{\"class\":\"Wizard\"}",
            currentStateJson: "{\"hp\":100}",
            campaignId: campaignId);

        Assert.Equal("Gandalf", character.Name);
        Assert.Equal("D&D 5e", character.GameSystem);
        Assert.Equal("2014", character.Ruleset);
        Assert.Equal(20, character.Level);
        Assert.Equal("{\"class\":\"Wizard\"}", character.CanonicalJson);
        Assert.Equal("{\"hp\":100}", character.CurrentStateJson);
        Assert.Equal(campaignId, character.CampaignId);
        Assert.Null(character.OwnerParticipantId);
    }

    [Fact]
    public void Character_UpdatedAt_InitiallyEqualsCreatedAt()
    {
        var character = new Character("Hero", "D&D 5e", "2014", 1, "{}", "{}");
        Assert.Equal(character.CreatedAt, character.UpdatedAt);
    }

    [Fact]
    public void Character_NoCampaign_CampaignIdIsNull()
    {
        var character = new Character("Wanderer", "D&D 5e", "2014", 5, "{}", "{}");
        Assert.Null(character.CampaignId);
    }
}
