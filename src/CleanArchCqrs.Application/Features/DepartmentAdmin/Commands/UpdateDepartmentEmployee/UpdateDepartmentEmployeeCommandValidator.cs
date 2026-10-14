using FluentValidation;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.UpdateDepartmentEmployee;

public class UpdateDepartmentEmployeeCommandValidator : AbstractValidator<UpdateDepartmentEmployeeCommand>
{
    public UpdateDepartmentEmployeeCommandValidator()
    {
        RuleFor(v => v.DepartmentId)
            .NotEmpty().WithMessage("Department ID không được để trống.");

        RuleFor(v => v.UserId)
            .NotEmpty().WithMessage("User ID không được để trống.");

        RuleFor(v => v.FirstName)
            .NotEmpty().WithMessage("Tên (FirstName) không được để trống.")
            .MaximumLength(50).WithMessage("Tên không được vượt quá 50 ký tự.");

        RuleFor(v => v.LastName)
            .NotEmpty().WithMessage("Họ đệm (LastName) không được để trống.")
            .MaximumLength(50).WithMessage("Họ đệm không được vượt quá 50 ký tự.");

        RuleFor(v => v.PhoneNumber)
            .MaximumLength(20).WithMessage("Số điện thoại không được vượt quá 20 ký tự.")
            .Matches(@"^[0-9+\-\s()]*$").WithMessage("Số điện thoại chứa ký tự không hợp lệ.")
            .When(v => !string.IsNullOrEmpty(v.PhoneNumber));

        RuleFor(v => v.StorageQuotaBytes)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức lưu trữ không được là số âm.")
            .When(v => v.StorageQuotaBytes.HasValue);
    }
}
