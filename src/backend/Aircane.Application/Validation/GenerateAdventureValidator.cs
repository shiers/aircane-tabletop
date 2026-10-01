using Aircane.Application.DTOs.Adventures;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates a <see cref="GenerateAdventureRequest"/> before starting the generation pipeline.
/// </summary>
public sealed class GenerateAdventureValidator : AbstractValidator<GenerateAdventureRequest>
{
    private static readonly string[] ValidModes = ["Solo", "Group"];
    private static readonly string[] ValidTones = ["dark", "lighthearted", "epic", "horror", "comedic", "mysterious", "heroic"];
    private static readonly string[] ValidLengths = ["one-shot", "short", "medium", "long"];
    private static readonly string[] ValidDifficulties = ["easy", "medium", "hard", "deadly"];

    public GenerateAdventureValidator()
    {
        RuleFor(x => x.Mode)
            .NotEmpty().WithMessage("Mode is required.")
            .Must(m => ValidModes.Contains(m, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Mode must be 'Solo' or 'Group'.");

        RuleFor(x => x.Ruleset)
            .NotEmpty().WithMessage("Ruleset is required.")
            .MaximumLength(200).WithMessage("Ruleset must not exceed 200 characters.");

        RuleFor(x => x.GameSystem)
            .NotEmpty().WithMessage("Game system is required.")
            .MaximumLength(200).WithMessage("Game system must not exceed 200 characters.");

        RuleFor(x => x.PartySize)
            .GreaterThanOrEqualTo(1).WithMessage("Party size must be at least 1.")
            .LessThanOrEqualTo(10).WithMessage("Party size must not exceed 10.");

        RuleFor(x => x.PartySize)
            .GreaterThanOrEqualTo(2).WithMessage("Party size must be at least 2 for group adventures.")
            .When(x => string.Equals(x.Mode, "Group", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.AverageLevel)
            .InclusiveBetween(1, 20).WithMessage("Average level must be between 1 and 20.");

        RuleFor(x => x.Tone)
            .NotEmpty().WithMessage("Tone is required.")
            .Must(t => ValidTones.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Tone must be one of: {string.Join(", ", ValidTones)}.");

        RuleFor(x => x.Length)
            .NotEmpty().WithMessage("Length is required.")
            .Must(l => ValidLengths.Contains(l, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Length must be one of: {string.Join(", ", ValidLengths)}.");

        RuleFor(x => x.Difficulty)
            .NotEmpty().WithMessage("Difficulty is required.")
            .Must(d => ValidDifficulties.Contains(d, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Difficulty must be one of: {string.Join(", ", ValidDifficulties)}.");

        RuleFor(x => x.CombatRatio)
            .InclusiveBetween(0, 100).WithMessage("Combat ratio must be between 0 and 100.");

        RuleFor(x => x.ExplorationRatio)
            .InclusiveBetween(0, 100).WithMessage("Exploration ratio must be between 0 and 100.");

        RuleFor(x => x.RoleplayRatio)
            .InclusiveBetween(0, 100).WithMessage("Roleplay ratio must be between 0 and 100.");

        RuleFor(x => x)
            .Must(x => Math.Abs(x.CombatRatio + x.ExplorationRatio + x.RoleplayRatio - 100) <= 5)
            .WithName("Ratios")
            .WithMessage("Combat, exploration, and roleplay ratios must sum to approximately 100 (within ±5).");

        RuleFor(x => x.Setting)
            .MaximumLength(500).WithMessage("Setting must not exceed 500 characters.")
            .When(x => x.Setting is not null);

        RuleFor(x => x.CharacterIds)
            .Must(ids => ids!.Count <= 10).WithMessage("Cannot specify more than 10 character IDs.")
            .When(x => x.CharacterIds is not null && x.CharacterIds.Count > 0);
    }
}
