using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Users.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Users.Queries.GetMyStorageQuota;

/// <summary>
/// Handler xử lý việc truy vấn hạn mức lưu trữ của người dùng hiện tại.
/// </summary>
public class GetMyStorageQuotaQueryHandler : IRequestHandler<GetMyStorageQuotaQuery, Result<PersonalStorageQuotaDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetMyStorageQuotaQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PersonalStorageQuotaDto>> Handle(
        GetMyStorageQuotaQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.UserId.HasValue)
        {
            return Result<PersonalStorageQuotaDto>.Failure("Không tìm thấy thông tin xác thực của người dùng.");
        }

        var userId = _currentUserService.UserId.Value;

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return Result<PersonalStorageQuotaDto>.Failure("Người dùng không tồn tại hoặc đã bị khóa.");
        }

        var quota = await _context.UserStorageQuotas
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.UserId == userId, cancellationToken);

        if (quota == null)
        {
            return Result<PersonalStorageQuotaDto>.Failure("Người dùng chưa được phân bổ hạn mức lưu trữ cá nhân.");
        }

        var availableBytes = Math.Max(0, quota.QuotaBytes - quota.UsedBytes);
        var usagePercent = quota.QuotaBytes > 0 
            ? Math.Round((double)quota.UsedBytes / quota.QuotaBytes * 100, 2) 
            : 0;

        var dto = new PersonalStorageQuotaDto(
            UserId: user.Id,
            FullName: user.FullName,
            QuotaBytes: quota.QuotaBytes,
            UsedBytes: quota.UsedBytes,
            AvailableBytes: availableBytes,
            UsagePercent: usagePercent,
            DepartmentName: user.Department?.Name
        );

        return Result<PersonalStorageQuotaDto>.Success(dto);
    }
}
