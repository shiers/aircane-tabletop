using Aircane.Application.DTOs.Library;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates a <see cref="FolderImportSelectionRequest"/> before importing the host's selection.
/// Ensures the folder is identified, items are present, and any per-file overrides respect the
/// same length limits as direct uploads.
/// </summary>
public sealed class FolderImportSelectionValidator : AbstractValidator<FolderImportSelectionRequest>
{
    public FolderImportSelectionValidator()
    {
        RuleFor(x => x.FolderId)
            .NotEmpty().WithMessage("Folder id is required.");

        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items are required.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.SourcePath)
                .NotEmpty().WithMessage("Each item must have a source path.");

            item.RuleFor(i => i.Title)
                .MaximumLength(500).WithMessage("Title must not exceed 500 characters.")
                .When(i => i.Title is not null);

            item.RuleFor(i => i.GameSystem)
                .MaximumLength(100).WithMessage("Game system must not exceed 100 characters.")
                .When(i => i.GameSystem is not null);

            item.RuleFor(i => i.Ruleset)
                .MaximumLength(100).WithMessage("Ruleset must not exceed 100 characters.")
                .When(i => i.Ruleset is not null);
        });
    }
}
