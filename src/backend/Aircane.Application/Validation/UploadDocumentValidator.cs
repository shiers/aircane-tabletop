using Aircane.Application.DTOs.Library;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates the metadata portion of an upload document request.
/// File-level validation (size, MIME, extension) is handled in LibraryService
/// because it requires access to the stream.
/// </summary>
public sealed class UploadDocumentValidator : AbstractValidator<UploadDocumentRequest>
{
    public UploadDocumentValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.OriginalFileName)
            .NotEmpty().WithMessage("Original file name is required.")
            .MaximumLength(500).WithMessage("File name must not exceed 500 characters.");

        RuleFor(x => x.GameSystem)
            .NotEmpty().WithMessage("Game system is required.")
            .MaximumLength(100).WithMessage("Game system must not exceed 100 characters.");

        RuleFor(x => x.Ruleset)
            .NotEmpty().WithMessage("Ruleset is required.")
            .MaximumLength(100).WithMessage("Ruleset must not exceed 100 characters.");

        RuleFor(x => x.FileContent)
            .NotNull().WithMessage("File content is required.");
    }
}
