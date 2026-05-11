using Aircane.Application.DTOs.Sessions;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates a <see cref="CreateSessionRequest"/> before the session is persisted.
/// </summary>
public sealed class CreateSessionValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionValidator()
    {
        RuleFor(x => x.CampaignId)
            .NotEmpty().WithMessage("Campaign ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Session name is required.")
            .MaximumLength(200).WithMessage("Session name must not exceed 200 characters.");

        RuleFor(x => x.AccessMode)
            .IsInEnum().WithMessage("Access mode must be a valid SessionAccessMode value.");
    }
}
