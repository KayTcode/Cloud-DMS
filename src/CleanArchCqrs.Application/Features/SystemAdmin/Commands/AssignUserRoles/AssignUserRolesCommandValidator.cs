using FluentValidation;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.AssignUserRoles;

public class AssignUserRolesCommandValidator : AbstractValidator<AssignUserRolesCommand>
{
    public AssignUserRolesCommandValidator()
    {
        RuleFor(v => v.Id)
            .NotEmpty().WithMessage("User ID is required.");
    }
}
