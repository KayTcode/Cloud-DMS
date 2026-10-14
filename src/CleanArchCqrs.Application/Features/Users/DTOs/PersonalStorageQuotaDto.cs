namespace CleanArchCqrs.Application.Features.Users.DTOs;

/// <summary>
/// DTO đại diện cho thông tin hạn mức dung lượng cá nhân của người dùng.
/// </summary>
public record PersonalStorageQuotaDto(
    Guid UserId,
    string FullName,
    long QuotaBytes,
    long UsedBytes,
    long AvailableBytes,
    double UsagePercent,
    string? DepartmentName
);
