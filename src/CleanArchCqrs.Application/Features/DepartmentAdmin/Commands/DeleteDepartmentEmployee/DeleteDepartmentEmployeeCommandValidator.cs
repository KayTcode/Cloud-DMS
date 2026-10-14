using FluentValidation;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.DeleteDepartmentEmployee;

public class DeleteDepartmentEmployeeCommandValidator : AbstractValidator<DeleteDepartmentEmployeeCommand>
{
    public DeleteDepartmentEmployeeCommandValidator()
    {
        RuleFor(v => v.DepartmentId)
            .NotEmpty().WithMessage("Department ID không được để trống.");

        RuleFor(v => v.UserId)
            .NotEmpty().WithMessage("User ID không được để trống.");
    }
}
