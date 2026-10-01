using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates a <see cref="GameSystemDefinition"/> ensuring all required metadata,
/// dice conventions, resolution rules, character schema, conditions, action economy,
/// and encounter budget sections are structurally valid and internally consistent.
/// </summary>
public sealed class GameSystemDefinitionValidator : AbstractValidator<GameSystemDefinition>
{
    public GameSystemDefinitionValidator()
    {
        // Required metadata fields
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Identifier is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.");

        RuleFor(x => x.Version)
            .NotEmpty().WithMessage("Version is required.");

        RuleFor(x => x.SchemaVersion)
            .Equal(1).WithMessage("SchemaVersion must be 1.");

        // Dice conventions validation
        RuleForEach(x => x.DiceConventions)
            .SetValidator(new DiceConventionValidator());

        // Resolution rules validation (with cross-reference to dice conventions)
        RuleFor(x => x)
            .Custom((definition, context) =>
            {
                var conventionNames = definition.DiceConventions
                    .Select(dc => dc.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < definition.ResolutionRules.Count; i++)
                {
                    var rule = definition.ResolutionRules[i];

                    if (string.IsNullOrWhiteSpace(rule.Name))
                    {
                        context.AddFailure(
                            $"ResolutionRules[{i}].Name",
                            "Resolution rule name is required.");
                    }

                    if (!Enum.IsDefined(rule.Type))
                    {
                        context.AddFailure(
                            $"ResolutionRules[{i}].Type",
                            "Resolution rule type must be a valid ResolutionRuleType value.");
                    }

                    if (string.IsNullOrWhiteSpace(rule.Roll))
                    {
                        context.AddFailure(
                            $"ResolutionRules[{i}].Roll",
                            "Resolution rule roll reference is required.");
                    }
                    else if (conventionNames.Count > 0 && !conventionNames.Contains(rule.Roll))
                    {
                        context.AddFailure(
                            $"ResolutionRules[{i}].Roll",
                            $"Resolution rule roll '{rule.Roll}' does not reference an existing dice convention name.");
                    }
                }
            });

        // Character schema validation
        When(x => x.CharacterSchema is not null, () =>
        {
            RuleFor(x => x.CharacterSchema!)
                .SetValidator(new CharacterSchemaValidator());
        });

        // Condition definitions validation
        RuleForEach(x => x.ConditionSet)
            .SetValidator(new ConditionDefinitionValidator());

        // Action economy validation
        When(x => x.ActionEconomy is not null, () =>
        {
            RuleFor(x => x.ActionEconomy!)
                .SetValidator(new ActionEconomyDefinitionValidator());
        });

        // Encounter budget validation
        When(x => x.EncounterBudget is not null, () =>
        {
            RuleFor(x => x.EncounterBudget!)
                .SetValidator(new EncounterBudgetFormulaValidator());
        });
    }
}

/// <summary>
/// Validates a single <see cref="DiceConvention"/> entry.
/// </summary>
internal sealed class DiceConventionValidator : AbstractValidator<DiceConvention>
{
    public DiceConventionValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Dice convention name is required.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Dice convention type must be a valid DiceConventionType value.");
    }
}

/// <summary>
/// Validates a <see cref="CharacterSchema"/> including its sections and fields.
/// </summary>
internal sealed class CharacterSchemaValidator : AbstractValidator<CharacterSchema>
{
    public CharacterSchemaValidator()
    {
        RuleForEach(x => x.Sections)
            .SetValidator(new CharacterSchemaSectionValidator());
    }
}

/// <summary>
/// Validates a single <see cref="CharacterSchemaSection"/>.
/// </summary>
internal sealed class CharacterSchemaSectionValidator : AbstractValidator<CharacterSchemaSection>
{
    public CharacterSchemaSectionValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Section id is required.");

        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("Section label is required.");

        RuleForEach(x => x.Fields)
            .SetValidator(new CharacterSchemaFieldValidator());
    }
}

/// <summary>
/// Validates a single <see cref="CharacterSchemaField"/>.
/// </summary>
internal sealed class CharacterSchemaFieldValidator : AbstractValidator<CharacterSchemaField>
{
    public CharacterSchemaFieldValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Field id is required.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Field type must be a valid CharacterFieldType value.");
    }
}

/// <summary>
/// Validates a single <see cref="ConditionDefinition"/>.
/// </summary>
internal sealed class ConditionDefinitionValidator : AbstractValidator<ConditionDefinition>
{
    public ConditionDefinitionValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Condition name is required.");

        RuleFor(x => x.DurationType)
            .NotEmpty().WithMessage("Condition duration type is required.");
    }
}

/// <summary>
/// Validates an <see cref="ActionEconomyDefinition"/>.
/// </summary>
internal sealed class ActionEconomyDefinitionValidator : AbstractValidator<ActionEconomyDefinition>
{
    public ActionEconomyDefinitionValidator()
    {
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Action economy type must be a valid ActionEconomyType value.");

        // When type is NamedSlots, TurnStructure must be present with at least one slot
        When(x => x.Type == ActionEconomyType.NamedSlots, () =>
        {
            RuleFor(x => x.TurnStructure)
                .NotNull().WithMessage("TurnStructure is required when action economy type is NamedSlots.");

            When(x => x.TurnStructure is not null, () =>
            {
                RuleFor(x => x.TurnStructure!.Slots)
                    .NotEmpty().WithMessage("TurnStructure must have at least one slot when action economy type is NamedSlots.");
            });
        });
    }
}

/// <summary>
/// Validates an <see cref="EncounterBudgetFormula"/>.
/// </summary>
internal sealed class EncounterBudgetFormulaValidator : AbstractValidator<EncounterBudgetFormula>
{
    public EncounterBudgetFormulaValidator()
    {
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Encounter budget type must be a valid EncounterBudgetType value.");

        RuleFor(x => x.DifficultyTiers)
            .NotEmpty().WithMessage("Encounter budget must have at least one difficulty tier.");
    }
}
