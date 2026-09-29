using FluentValidation;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.UpdateUser;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(v => v.Id)
            .NotEmpty().WithMessage("User ID is required.");

        RuleFor(v => v.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(v => v.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(v => v.PhoneNumber)
            .MaximumLength(25).WithMessage("Phone number must not exceed 25 characters.")
            .When(v => !string.IsNullOrEmpty(v.PhoneNumber));
    }
}
