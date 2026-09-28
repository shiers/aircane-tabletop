using Aircane.Infrastructure.Adventures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>
/// Unit tests for <see cref="EncounterValidatorSelector"/>: picks the PF2e validator for PF2e
/// systems and falls back to D&amp;D 5e otherwise.
/// </summary>
public class EncounterValidatorSelectorTests
{
    private readonly EncounterValidatorSelector _sut = new(
        new Dnd5eEncounterValidator(NullLogger<Dnd5eEncounterValidator>.Instance),
        new Pf2eEncounterValidator(NullLogger<Pf2eEncounterValidator>.Instance));

    [Theory]
    [InlineData("pathfinder-2e-remaster")]
    [InlineData("Pathfinder 2e")]
    [InlineData("Pathfinder Second Edition")]
    [InlineData("PF2e")]
    public void ForGameSystem_Pf2eVariants_ReturnsPf2eValidator(string system)
    {
        Assert.IsType<Pf2eEncounterValidator>(_sut.ForGameSystem(system));
    }

    [Theory]
    [InlineData("dnd-5e-2014")]
    [InlineData("D&D 5e 2014")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("some-unknown-system")]
    public void ForGameSystem_NonPf2e_ReturnsDnd5eValidator(string? system)
    {
        Assert.IsType<Dnd5eEncounterValidator>(_sut.ForGameSystem(system));
    }
}
