using Aircane.Application.DTOs.Library;
using Aircane.Domain.Enums;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates an <see cref="UpdateFolderRequest"/> before the folder record is updated.
/// All fields are optional; only provided (non-null) fields are validated.
/// Includes path safety checks when a new path is provided.
/// </summary>
public sealed class UpdateFolderValidator : AbstractValidator<UpdateFolderRequest>
{
    private static readonly SourceType[] InvalidDefaultSourceTypes =
        [SourceType.Generated, SourceType.Unknown];

    public UpdateFolderValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name must not be empty when provided.")
            .MaximumLength(300).WithMessage("Display name must not exceed 300 characters.")
            .When(x => x.DisplayName is not null);

        RuleFor(x => x.AbsolutePath)
            .NotEmpty().WithMessage("Absolute path must not be empty when provided.")
            .Must(p => FolderPathValidator.IsAbsolutePath(p!)).WithMessage("Path must be an absolute path.")
            .Must(p => !FolderPathValidator.ContainsTraversalSequence(p!))
                .WithMessage("Path must not contain path traversal sequences (..).")
            .Must(p => !FolderPathValidator.IsSystemDirectory(p!))
                .WithMessage("Path must not be a system directory.")
            .Must(p => Directory.Exists(p!))
                .WithMessage("Path does not exist or is not accessible.")
            .When(x => x.AbsolutePath is not null);

        RuleFor(x => x.DefaultSourceType)
            .Must(t => !InvalidDefaultSourceTypes.Contains(t!.Value))
            .WithMessage($"Default source type must not be {SourceType.Generated} or {SourceType.Unknown}.")
            .When(x => x.DefaultSourceType.HasValue);

        RuleFor(x => x.DefaultGameSystem)
            .MaximumLength(100).WithMessage("Game system must not exceed 100 characters.")
            .When(x => x.DefaultGameSystem is not null);

        RuleFor(x => x.DefaultRuleset)
            .MaximumLength(100).WithMessage("Ruleset must not exceed 100 characters.")
            .When(x => x.DefaultRuleset is not null);
    }
}
