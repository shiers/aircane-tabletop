using Aircane.Application.DTOs.Library;
using Aircane.Domain.Enums;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates a <see cref="RegisterFolderRequest"/> before the folder is persisted.
/// Includes path safety checks: absolute path, no traversal sequences, not a system directory,
/// and directory must exist and be accessible.
/// </summary>
public sealed class RegisterFolderValidator : AbstractValidator<RegisterFolderRequest>
{
    /// <summary>
    /// Source types that are not valid defaults for a watched folder.
    /// Generated and Unknown are assigned by the system, not by the host at registration time.
    /// </summary>
    private static readonly SourceType[] InvalidDefaultSourceTypes =
        [SourceType.Generated, SourceType.Unknown];

    public RegisterFolderValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(300).WithMessage("Display name must not exceed 300 characters.");

        RuleFor(x => x.AbsolutePath)
            .NotEmpty().WithMessage("Absolute path is required.")
            .Must(FolderPathValidator.IsAbsolutePath).WithMessage("Path must be an absolute path.")
            .Must(p => !FolderPathValidator.ContainsTraversalSequence(p))
                .WithMessage("Path must not contain path traversal sequences (..).")
            .Must(p => !FolderPathValidator.IsSystemDirectory(p))
                .WithMessage("Path must not be a system directory.")
            .Must(Directory.Exists)
                .WithMessage("Path does not exist or is not accessible.");

        RuleFor(x => x.DefaultSourceType)
            .Must(t => !InvalidDefaultSourceTypes.Contains(t))
            .WithMessage($"Default source type must not be {SourceType.Generated} or {SourceType.Unknown}.");

        RuleFor(x => x.DefaultGameSystem)
            .MaximumLength(100).WithMessage("Game system must not exceed 100 characters.")
            .When(x => x.DefaultGameSystem is not null);

        RuleFor(x => x.DefaultRuleset)
            .MaximumLength(100).WithMessage("Ruleset must not exceed 100 characters.")
            .When(x => x.DefaultRuleset is not null);
    }
}
