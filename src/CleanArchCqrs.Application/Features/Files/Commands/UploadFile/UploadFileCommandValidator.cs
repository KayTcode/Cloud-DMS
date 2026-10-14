using FluentValidation;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".php", ".vbs", ".js", ".ps1",
        ".msi", ".dll", ".jar", ".com", ".pif", ".scr", ".hta", ".jsp",
        ".aspx", ".cgi", ".config"
    };

    public UploadFileCommandValidator()
    {
        RuleFor(v => v.FileStream)
            .NotNull()
            .WithMessage("Luồng dữ liệu tệp tin không được để trống.");

        RuleFor(v => v.FileSize)
            .GreaterThan(0)
            .WithMessage("Kích thước tệp tin phải lớn hơn 0 bytes.")
            .LessThanOrEqualTo(100L * 1024 * 1024)
            .WithMessage("Kích thước tệp tin không được vượt quá 100 MB.");

        RuleFor(v => v.FileName)
            .NotEmpty()
            .WithMessage("Tên tệp tin không được để trống.")
            .MaximumLength(255)
            .WithMessage("Tên tệp tin không được vượt quá 255 ký tự.")
            .Must(name => !string.IsNullOrEmpty(name) && !name.Contains("..") && !name.Contains('/') && !name.Contains('\\'))
            .WithMessage("Tên tệp tin chứa ký tự điều hướng đường dẫn không hợp lệ.")
            .Must(name =>
            {
                if (string.IsNullOrEmpty(name)) return false;
                var ext = Path.GetExtension(name);
                return string.IsNullOrEmpty(ext) || !BlockedExtensions.Contains(ext);
            })
            .WithMessage(v => $"Tệp tin '{v.FileName}' có định dạng không được phép tải lên hệ thống vì lý do bảo mật.");

        RuleFor(v => v.ContentType)
            .MaximumLength(255)
            .WithMessage("Định dạng Content-Type không được vượt quá 255 ký tự.");
    }
}
