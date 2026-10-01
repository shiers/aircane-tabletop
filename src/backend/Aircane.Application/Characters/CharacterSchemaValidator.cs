using FluentValidation;

namespace Aircane.Application.Characters;

/// <summary>
/// Validates a <see cref="CanonicalCharacter"/> against the canonical schema rules.
/// Used when importing a character from JSON or before persisting a manually created character.
/// </summary>
public sealed class CharacterSchemaValidator : AbstractValidator<CanonicalCharacter>
{
    private const int MinLevel = 1;
    private const int MaxLevel = 20;
    private const int MinAbilityScore = 1;
    private const int MaxAbilityScore = 30;
    private const int MinHitPoints = 0;

    public CharacterSchemaValidator()
    {
        // Identity
        RuleFor(c => c.Identity)
            .NotNull().WithMessage("Identity is required.");

        RuleFor(c => c.Identity.Name)
            .NotEmpty().WithMessage("Character name is required.")
            .MaximumLength(200).WithMessage("Character name must not exceed 200 characters.");

        // Classes
        RuleFor(c => c.Classes)
            .NotEmpty().WithMessage("At least one class is required.");

        RuleForEach(c => c.Classes).ChildRules(cls =>
        {
            cls.RuleFor(c => c.ClassName)
                .NotEmpty().WithMessage("Class name is required.");

            cls.RuleFor(c => c.Level)
                .InclusiveBetween(MinLevel, MaxLevel)
                .WithMessage($"Class level must be between {MinLevel} and {MaxLevel}.");

            cls.RuleFor(c => c.HitDie)
                .GreaterThan(0).WithMessage("Hit die must be a positive number.");
        });

        // Total level across all classes must be 1–20
        RuleFor(c => c.Classes)
            .Must(classes => classes.Sum(cl => cl.Level) is >= MinLevel and <= MaxLevel)
            .WithMessage($"Total character level (sum of all class levels) must be between {MinLevel} and {MaxLevel}.")
            .When(c => c.Classes.Count > 0);

        // Ability scores
        RuleFor(c => c.Abilities.Strength)
            .InclusiveBetween(MinAbilityScore, MaxAbilityScore)
            .WithMessage($"Strength must be between {MinAbilityScore} and {MaxAbilityScore}.");

        RuleFor(c => c.Abilities.Dexterity)
            .InclusiveBetween(MinAbilityScore, MaxAbilityScore)
            .WithMessage($"Dexterity must be between {MinAbilityScore} and {MaxAbilityScore}.");

        RuleFor(c => c.Abilities.Constitution)
            .InclusiveBetween(MinAbilityScore, MaxAbilityScore)
            .WithMessage($"Constitution must be between {MinAbilityScore} and {MaxAbilityScore}.");

        RuleFor(c => c.Abilities.Intelligence)
            .InclusiveBetween(MinAbilityScore, MaxAbilityScore)
            .WithMessage($"Intelligence must be between {MinAbilityScore} and {MaxAbilityScore}.");

        RuleFor(c => c.Abilities.Wisdom)
            .InclusiveBetween(MinAbilityScore, MaxAbilityScore)
            .WithMessage($"Wisdom must be between {MinAbilityScore} and {MaxAbilityScore}.");

        RuleFor(c => c.Abilities.Charisma)
            .InclusiveBetween(MinAbilityScore, MaxAbilityScore)
            .WithMessage($"Charisma must be between {MinAbilityScore} and {MaxAbilityScore}.");

        // Combat HP
        RuleFor(c => c.Combat.MaxHitPoints)
            .GreaterThanOrEqualTo(MinHitPoints)
            .WithMessage($"Max hit points must be {MinHitPoints} or greater.");

        RuleFor(c => c.Combat.CurrentHitPoints)
            .GreaterThanOrEqualTo(MinHitPoints)
            .WithMessage($"Current hit points must be {MinHitPoints} or greater.");

        RuleFor(c => c.Combat.TemporaryHitPoints)
            .GreaterThanOrEqualTo(MinHitPoints)
            .WithMessage($"Temporary hit points must be {MinHitPoints} or greater.");

        // Spell slots
        When(c => c.Spells != null, () =>
        {
            RuleForEach(c => c.Spells!.SpellSlots).ChildRules(slot =>
            {
                slot.RuleFor(s => s.Level)
                    .InclusiveBetween(1, 9)
                    .WithMessage("Spell slot level must be between 1 and 9.");

                slot.RuleFor(s => s.Total)
                    .GreaterThanOrEqualTo(0)
                    .WithMessage("Spell slot total must be 0 or greater.");
            });
        });

        // Resources
        RuleForEach(c => c.Resources).ChildRules(res =>
        {
            res.RuleFor(r => r.Name)
                .NotEmpty().WithMessage("Resource name is required.");

            res.RuleFor(r => r.Maximum)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Resource maximum must be 0 or greater.");

            res.RuleFor(r => r.Current)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Resource current value must be 0 or greater.");
        });

        // Inventory
        RuleForEach(c => c.Inventory).ChildRules(item =>
        {
            item.RuleFor(i => i.Name)
                .NotEmpty().WithMessage("Inventory item name is required.");

            item.RuleFor(i => i.Quantity)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Inventory item quantity must be 0 or greater.");
        });
    }
}
