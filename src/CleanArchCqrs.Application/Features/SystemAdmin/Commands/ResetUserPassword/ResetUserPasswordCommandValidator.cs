using FluentValidation;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.ResetUserPassword;

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(v => v.Id)
            .NotEmpty().WithMessage("User ID is required.");

        RuleFor(v => v.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(6).WithMessage("New password must be at least 6 characters long.")
            .MaximumLength(100).WithMessage("New password must not exceed 100 characters.");
    }
}
