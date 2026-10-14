using FluentValidation;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.CreateEmployee;

public class CreateEmployeeCommandValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeCommandValidator()
    {
        RuleFor(v => v.DepartmentId)
            .NotEmpty().WithMessage("Mã phòng ban (DepartmentId) là bắt buộc.");

        RuleFor(v => v.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Định dạng email không hợp lệ.")
            .MaximumLength(256).WithMessage("Email không được vượt quá 256 ký tự.");

        RuleFor(v => v.FirstName)
            .NotEmpty().WithMessage("Tên (FirstName) không được để trống.")
            .MaximumLength(100).WithMessage("Tên không được vượt quá 100 ký tự.");

        RuleFor(v => v.LastName)
            .NotEmpty().WithMessage("Họ (LastName) không được để trống.")
            .MaximumLength(100).WithMessage("Họ không được vượt quá 100 ký tự.");

        RuleFor(v => v.PhoneNumber)
            .MaximumLength(25).WithMessage("Số điện thoại không được vượt quá 25 ký tự.")
            .When(v => !string.IsNullOrEmpty(v.PhoneNumber));

        RuleFor(v => v.StorageQuotaBytes)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức lưu trữ không được là số âm.")
            .When(v => v.StorageQuotaBytes.HasValue);
    }
}
