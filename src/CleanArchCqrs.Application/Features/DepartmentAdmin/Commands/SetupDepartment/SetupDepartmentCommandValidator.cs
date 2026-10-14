using FluentValidation;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.SetupDepartment;

public class SetupDepartmentCommandValidator : AbstractValidator<SetupDepartmentCommand>
{
    public SetupDepartmentCommandValidator()
    {
        RuleFor(v => v.Name)
            .NotEmpty().WithMessage("Tên phòng ban không được để trống.")
            .Length(2, 100).WithMessage("Tên phòng ban phải có độ dài từ 2 đến 100 ký tự.");

        RuleFor(v => v.Code)
            .NotEmpty().WithMessage("Mã phòng ban không được để trống.")
            .Matches(@"^[a-zA-Z0-9_-]{2,50}$")
            .WithMessage("Mã phòng ban chỉ được chứa chữ cái, chữ số, dấu gạch nối và gạch dưới, độ dài từ 2 đến 50 ký tự.");

        RuleFor(v => v.Description)
            .MaximumLength(500).WithMessage("Mô tả phòng ban không được vượt quá 500 ký tự.")
            .When(v => !string.IsNullOrEmpty(v.Description));

        RuleFor(v => v.AdminPersonalQuotaGB)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức cá nhân giữ lại (GB) không được là số âm.")
            .When(v => v.AdminPersonalQuotaGB.HasValue);

        RuleFor(v => v.AdminPersonalQuotaBytes)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức cá nhân giữ lại (Bytes) không được là số âm.")
            .When(v => v.AdminPersonalQuotaBytes.HasValue);
    }
}
