using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

/// <summary>
/// Verifies that all domain enums define the expected named values.
/// These tests guard against accidental renames or removals that would break
/// serialized data or API contracts.
/// </summary>
public class EnumTests
{
    [Fact]
    public void SourceType_HasExpectedValues()
    {
        var values = Enum.GetNames<SourceType>();
        Assert.Contains(nameof(SourceType.Rules), values);
        Assert.Contains(nameof(SourceType.Adventure), values);
        Assert.Contains(nameof(SourceType.Solo), values);
        Assert.Contains(nameof(SourceType.Character), values);
        Assert.Contains(nameof(SourceType.Homebrew), values);
        Assert.Contains(nameof(SourceType.Generated), values);
        Assert.Contains(nameof(SourceType.Unknown), values);
    }

    [Fact]
    public void ImportStatus_HasExpectedValues()
    {
        var values = Enum.GetNames<ImportStatus>();
        Assert.Contains(nameof(ImportStatus.Pending), values);
        Assert.Contains(nameof(ImportStatus.Processing), values);
        Assert.Contains(nameof(ImportStatus.Completed), values);
        Assert.Contains(nameof(ImportStatus.Failed), values);
        Assert.Contains(nameof(ImportStatus.OcrRequired), values);
    }

    [Fact]
    public void ContentVisibility_HasExpectedValues()
    {
        var values = Enum.GetNames<ContentVisibility>();
        Assert.Contains(nameof(ContentVisibility.Public), values);
        Assert.Contains(nameof(ContentVisibility.DMOnly), values);
        Assert.Contains(nameof(ContentVisibility.Hidden), values);
        Assert.Contains(nameof(ContentVisibility.Revealed), values);
    }

    [Fact]
    public void AiRole_HasExpectedValues()
    {
        var values = Enum.GetNames<AiRole>();
        Assert.Contains(nameof(AiRole.Assistant), values);
        Assert.Contains(nameof(AiRole.CoDm), values);
        Assert.Contains(nameof(AiRole.FullDm), values);
        Assert.Contains(nameof(AiRole.Hybrid), values);
    }

    [Fact]
    public void AiAuthority_HasExpectedValues()
    {
        var values = Enum.GetNames<AiAuthority>();
        Assert.Contains(nameof(AiAuthority.SuggestOnly), values);
        Assert.Contains(nameof(AiAuthority.AskBeforeApplying), values);
        Assert.Contains(nameof(AiAuthority.AutoApplySafeActions), values);
        Assert.Contains(nameof(AiAuthority.FullSessionControl), values);
    }

    [Fact]
    public void SessionAccessMode_HasExpectedValues()
    {
        var values = Enum.GetNames<SessionAccessMode>();
        Assert.Contains(nameof(SessionAccessMode.Solo), values);
        Assert.Contains(nameof(SessionAccessMode.LocalLan), values);
        Assert.Contains(nameof(SessionAccessMode.InternetTunnel), values);
        Assert.Contains(nameof(SessionAccessMode.Cloud), values);
    }

    [Fact]
    public void SessionStatus_HasExpectedValues()
    {
        var values = Enum.GetNames<SessionStatus>();
        Assert.Contains(nameof(SessionStatus.Pending), values);
        Assert.Contains(nameof(SessionStatus.Active), values);
        Assert.Contains(nameof(SessionStatus.Paused), values);
        Assert.Contains(nameof(SessionStatus.Ended), values);
    }

    [Fact]
    public void ParticipantRole_HasExpectedValues()
    {
        var values = Enum.GetNames<ParticipantRole>();
        Assert.Contains(nameof(ParticipantRole.Host), values);
        Assert.Contains(nameof(ParticipantRole.HumanDm), values);
        Assert.Contains(nameof(ParticipantRole.Player), values);
        Assert.Contains(nameof(ParticipantRole.Spectator), values);
    }

    [Fact]
    public void RollVisibility_HasExpectedValues()
    {
        var values = Enum.GetNames<RollVisibility>();
        Assert.Contains(nameof(RollVisibility.Public), values);
        Assert.Contains(nameof(RollVisibility.Private), values);
    }
}
