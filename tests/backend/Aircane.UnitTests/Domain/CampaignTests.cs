using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class CampaignTests
{
    [Fact]
    public void Campaign_Constructor_SetsAllProperties()
    {
        var campaign = new Campaign(
            name: "The Lost Mines",
            gameSystem: "D&D 5e",
            ruleset: "2014",
            aiRole: AiRole.CoDm,
            aiAuthority: AiAuthority.AskBeforeApplying);

        Assert.Equal("The Lost Mines", campaign.Name);
        Assert.Equal("D&D 5e", campaign.GameSystem);
        Assert.Equal("2014", campaign.Ruleset);
        Assert.Equal(AiRole.CoDm, campaign.AiRole);
        Assert.Equal(AiAuthority.AskBeforeApplying, campaign.AiAuthority);
        Assert.Null(campaign.ActiveAdventureId);
    }

    [Fact]
    public void Campaign_Constructor_AssignsUniqueId()
    {
        var a = new Campaign("A", "D&D 5e", "2014");
        var b = new Campaign("B", "D&D 5e", "2014");

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Campaign_Constructor_SetsCreatedAtAndUpdatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var campaign = new Campaign("Test", "D&D 5e", "2014");
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(campaign.CreatedAt, before, after);
        Assert.InRange(campaign.UpdatedAt, before, after);
    }

    [Fact]
    public void Campaign_DefaultAiRole_IsAssistant()
    {
        var campaign = new Campaign("Test", "D&D 5e", "2014");
        Assert.Equal(AiRole.Assistant, campaign.AiRole);
    }

    [Fact]
    public void Campaign_DefaultAiAuthority_IsSuggestOnly()
    {
        var campaign = new Campaign("Test", "D&D 5e", "2014");
        Assert.Equal(AiAuthority.SuggestOnly, campaign.AiAuthority);
    }

    [Fact]
    public void Campaign_ActiveAdventureId_CanBeSet()
    {
        var adventureId = Guid.NewGuid();
        var campaign = new Campaign("Test", "D&D 5e", "2014", activeAdventureId: adventureId);
        Assert.Equal(adventureId, campaign.ActiveAdventureId);
    }
}
