using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.SetupDepartment;

public record SetupDepartmentCommand : IRequest<Result<DepartmentDto>>
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public double? AdminPersonalQuotaGB { get; init; }
    public long? AdminPersonalQuotaBytes { get; init; }
}

public class SetupDepartmentCommandHandler : IRequestHandler<SetupDepartmentCommand, Result<DepartmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SetupDepartmentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DepartmentDto>> Handle(
        SetupDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        var tenantId = _currentUserService.TenantId;

        if (!userId.HasValue || !tenantId.HasValue)
        {
            return Result<DepartmentDto>.Failure("Yêu cầu không hợp lệ: Chưa xác thực danh tính người dùng hoặc tổ chức.");
        }

        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        if (user == null)
        {
            return Result<DepartmentDto>.Failure("Không tìm thấy thông tin người dùng.");
        }

        var roles = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role.Name)
            .ToList();

        if (!roles.Contains("DepartmentAdmin"))
        {
            return Result<DepartmentDto>.Failure("Chỉ tài khoản Trưởng phòng (Department Admin) mới có quyền tự thiết lập phòng ban.");
        }

        if (user.DepartmentId.HasValue)
        {
            return Result<DepartmentDto>.Failure("Tài khoản của bạn đã được gắn với một Phòng ban, không thể thiết lập thêm phòng ban mới.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        // 1. Kiểm tra mã phòng ban trùng lặp trong Tenant
        var codeExists = await _context.Departments
            .AnyAsync(d => d.TenantId == tenantId.Value && d.Code == normalizedCode, cancellationToken);

        if (codeExists)
        {
            return Result<DepartmentDto>.Failure($"Mã phòng ban '{normalizedCode}' đã tồn tại trong tổ chức của bạn.");
        }

        // 2. Lấy vùng nhớ Quota mà Tenant Admin đã giao cho Department Admin này nắm giữ
        var adminQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == user.Id, cancellationToken);

        long departmentQuotaBytes = adminQuota?.QuotaBytes ?? (10L * 1024 * 1024 * 1024); // Mặc định 10 GB nếu chưa có

        // 3. Khởi tạo Phòng ban mới
        var department = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            Name = request.Name.Trim(),
            Code = normalizedCode,
            Description = request.Description?.Trim(),
            StorageQuotaBytes = departmentQuotaBytes,
            StorageUsedBytes = 0,
            IsActive = true
        };

        await _context.Departments.AddAsync(department, cancellationToken);

        // 4. Gắn Trưởng phòng vào Phòng ban vừa tạo
        user.DepartmentId = department.Id;
        user.Department = department;

        // 5. Cập nhật Hạn mức cá nhân do Trưởng phòng tự chọn giữ lại cho mình
        long personalQuota;
        if (request.AdminPersonalQuotaGB.HasValue)
        {
            if (request.AdminPersonalQuotaGB.Value < 0)
            {
                return Result<DepartmentDto>.Failure("Hạn mức cá nhân giữ lại không được là số âm.");
            }
            personalQuota = (long)(request.AdminPersonalQuotaGB.Value * 1024 * 1024 * 1024);
        }
        else if (request.AdminPersonalQuotaBytes.HasValue)
        {
            if (request.AdminPersonalQuotaBytes.Value < 0)
            {
                return Result<DepartmentDto>.Failure("Hạn mức cá nhân giữ lại không được là số âm.");
            }
            personalQuota = request.AdminPersonalQuotaBytes.Value;
        }
        else
        {
            // Mặc định giữ lại 1 GB (hoặc toàn bộ nếu phòng ban nhỏ hơn 1 GB)
            personalQuota = Math.Min(departmentQuotaBytes, 1L * 1024 * 1024 * 1024);
        }

        // Kiểm tra: Hạn mức cá nhân giữ lại không được vượt quá tổng dung lượng của phòng ban
        if (personalQuota > departmentQuotaBytes)
        {
            return Result<DepartmentDto>.Failure(
                $"Hạn mức cá nhân giữ lại ({FormatBytes(personalQuota)}) không được vượt quá tổng dung lượng của phòng ban ({FormatBytes(departmentQuotaBytes)}).");
        }

        if (adminQuota != null)
        {
            adminQuota.QuotaBytes = personalQuota;
        }
        else
        {
            adminQuota = new UserStorageQuota
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                QuotaBytes = personalQuota,
                UsedBytes = 0
            };
            await _context.UserStorageQuotas.AddAsync(adminQuota, cancellationToken);
        }

        // 6. Tự động tạo Thư mục gốc mặc định cho phòng ban
        var rootDeptFolder = new Folder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            DepartmentId = department.Id,
            Name = department.Name,
            ParentFolderId = null
        };
        await _context.Folders.AddAsync(rootDeptFolder, cancellationToken);

        // 7. Ghi AuditLog
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            UserId = user.Id,
            Action = "SetupDepartment",
            EntityName = "Department",
            EntityId = department.Id,
            Details = $"Trưởng phòng '{user.FullName}' đã tự thiết lập phòng ban '{department.Name}' ({department.Code}) với tổng vùng nhớ {FormatBytes(departmentQuotaBytes)}. Hạn mức cá nhân Trưởng phòng: {FormatBytes(personalQuota)}.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);

        var dto = new DepartmentDto
        {
            Id = department.Id,
            TenantId = department.TenantId,
            Name = department.Name,
            Code = department.Code,
            Description = department.Description,
            IsActive = department.IsActive,
            HeadCount = 1,
            Lead = user.FullName,
            AdminEmail = user.Email,
            StorageQuotaBytes = department.StorageQuotaBytes,
            StorageUsedBytes = department.StorageUsedBytes,
            CreatedAt = department.CreatedAt
        };

        return Result<DepartmentDto>.Success(dto);
    }
}
