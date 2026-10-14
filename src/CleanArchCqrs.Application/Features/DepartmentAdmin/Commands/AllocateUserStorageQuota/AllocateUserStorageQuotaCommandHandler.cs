using System.Data;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.AllocateUserStorageQuota;

public class AllocateUserStorageQuotaCommandHandler : IRequestHandler<AllocateUserStorageQuotaCommand, Result<UserStorageQuotaDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AllocateUserStorageQuotaCommandHandler> _logger;

    public AllocateUserStorageQuotaCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<AllocateUserStorageQuotaCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<UserStorageQuotaDto>> Handle(
        AllocateUserStorageQuotaCommand request,
        CancellationToken cancellationToken)
    {
        if (request.QuotaBytes < 0)
        {
            return Result<UserStorageQuotaDto>.Failure("Hạn mức lưu trữ không được là số âm.");
        }

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<UserStorageQuotaDto>.Failure("Không tìm thấy thông tin Phòng ban.");
        }

        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        var isTenantAdmin = _currentUserService.Role == "TenantAdmin";

        if (!isSystemAdmin)
        {
            var callerTenantId = _currentUserService.TenantId;
            if (!callerTenantId.HasValue && _currentUserService.UserId.HasValue)
            {
                callerTenantId = await _context.Users
                    .Where(u => u.Id == _currentUserService.UserId.Value)
                    .Select(u => u.TenantId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!callerTenantId.HasValue || department.TenantId != callerTenantId.Value)
            {
                return Result<UserStorageQuotaDto>.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }

            if (!isTenantAdmin)
            {
                var callerDeptId = _currentUserService.DepartmentId;
                if (!callerDeptId.HasValue && _currentUserService.UserId.HasValue)
                {
                    callerDeptId = await _context.Users
                        .Where(u => u.Id == _currentUserService.UserId.Value)
                        .Select(u => u.DepartmentId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (!callerDeptId.HasValue || callerDeptId.Value != department.Id)
                {
                    return Result<UserStorageQuotaDto>.Failure("Bạn không có quyền quản lý hạn mức của phòng ban này.");
                }
            }
        }

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            try
            {
                var user = await _context.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

                if (user == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<UserStorageQuotaDto>.Failure("Không tìm thấy thông tin người dùng.");
                }

                if (!isSystemAdmin && user.TenantId != department.TenantId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<UserStorageQuotaDto>.Failure("Người dùng không thuộc tổ chức này.");
                }

                if (!user.DepartmentId.HasValue || user.DepartmentId.Value != department.Id)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<UserStorageQuotaDto>.Failure("Người dùng không thuộc phòng ban được chỉ định.");
                }

                // 1. Lấy quota hiện tại của user (nếu có)
                var userQuota = await _context.UserStorageQuotas
                    .FirstOrDefaultAsync(q => q.UserId == user.Id, cancellationToken);

                // 2. Kiểm tra: Không được hạ quota thấp hơn dung lượng user đã thực tế sử dụng
                if (userQuota != null && request.QuotaBytes < userQuota.UsedBytes)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<UserStorageQuotaDto>.Failure(
                        $"Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng ({FormatBytes(userQuota.UsedBytes)}).");
                }

                // 3. Kiểm tra: Tổng hạn mức đã cấp cho tất cả user khác trong phòng ban + QuotaBytes mới <= Quota phòng ban
                var otherUsersQuotaSum = await _context.UserStorageQuotas
                    .Where(q => q.User.DepartmentId == department.Id && q.UserId != user.Id)
                    .SumAsync(q => q.QuotaBytes, cancellationToken);

                var availableInDept = Math.Max(0, department.StorageQuotaBytes - otherUsersQuotaSum);

                if (request.QuotaBytes > availableInDept)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<UserStorageQuotaDto>.Failure(
                        $"Phòng ban '{department.Name}' không còn đủ hạn mức để cấp cho nhân viên này! " +
                        $"Hạn mức phòng ban còn lại: {FormatBytes(availableInDept)} (Yêu cầu: {FormatBytes(request.QuotaBytes)}).");
                }

                long oldQuota = userQuota?.QuotaBytes ?? 0;

                if (userQuota == null)
                {
                    userQuota = new UserStorageQuota
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        QuotaBytes = request.QuotaBytes,
                        UsedBytes = 0
                    };
                    await _context.UserStorageQuotas.AddAsync(userQuota, cancellationToken);
                }
                else
                {
                    userQuota.QuotaBytes = request.QuotaBytes;
                }

                // Ghi AuditLog
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = user.TenantId,
                    UserId = _currentUserService.UserId,
                    Action = "AllocateUserQuota",
                    EntityName = "UserStorageQuota",
                    EntityId = user.Id,
                    Details = $"Điều chỉnh hạn mức lưu trữ của nhân viên '{user.FullName}' ({department.Name}) từ {FormatBytes(oldQuota)} thành {FormatBytes(request.QuotaBytes)}.",
                    CreatedAt = DateTime.UtcNow
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var dto = new UserStorageQuotaDto
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    DepartmentId = department.Id,
                    DepartmentName = department.Name,
                    QuotaBytes = userQuota.QuotaBytes,
                    UsedBytes = userQuota.UsedBytes
                };

                return Result<UserStorageQuotaDto>.Success(dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                _logger.LogError(ex, "Lỗi khi phân bổ hạn mức lưu trữ cho User {UserId} trong Department {DepartmentId}", request.UserId, request.DepartmentId);
                return Result<UserStorageQuotaDto>.Failure("Đã xảy ra lỗi trong quá trình điều phối hạn mức lưu trữ. Vui lòng thử lại sau.");
            }
        });
    }
}
