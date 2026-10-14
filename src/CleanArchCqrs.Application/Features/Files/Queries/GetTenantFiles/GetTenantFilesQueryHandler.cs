using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetTenantFiles;

public class GetTenantFilesQueryHandler : IRequestHandler<GetTenantFilesQuery, Result<TenantStorageSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetTenantFilesQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<TenantStorageSummaryDto>> Handle(GetTenantFilesQuery request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            return Result<TenantStorageSummaryDto>.Failure("Bạn chưa xác thực danh tính người dùng.");
        }

        var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value && u.IsActive, cancellationToken);
        if (currentUser == null)
        {
            return Result<TenantStorageSummaryDto>.Failure("Người dùng hiện tại không hợp lệ.");
        }

        var tenantId = _currentUserService.TenantId ?? currentUser.TenantId;
        if (!tenantId.HasValue)
        {
            return Result<TenantStorageSummaryDto>.Failure("Không xác định được tổ chức/tenant.");
        }

        if (request.TenantId.HasValue && request.TenantId.Value != tenantId.Value)
        {
            return Result<TenantStorageSummaryDto>.Failure("Bạn không có quyền xem dữ liệu của tenant khác.");
        }

        var tenant = await _context.Tenants
            .Include(t => t.StorageProvider)
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken);

        if (tenant == null)
        {
            return Result<TenantStorageSummaryDto>.Failure("Tenant không tồn tại.");
        }

        var query = _context.FileEntries
            .Include(f => f.Owner)
            .Include(f => f.StorageProvider)
            .Where(f => f.TenantId == tenant.Id && !f.IsDeleted);

        // Xác định Department Scope
        Guid? targetDepartmentId = request.DepartmentId;
        if (!targetDepartmentId.HasValue && (_currentUserService.Role == "DepartmentAdmin" || _currentUserService.Role == "Employee"))
        {
            targetDepartmentId = _currentUserService.DepartmentId ?? currentUser.DepartmentId;
        }

        CleanArchCqrs.Domain.Entities.Department? department = null;
        if (targetDepartmentId.HasValue)
        {
            department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == targetDepartmentId.Value && d.TenantId == tenant.Id && d.IsActive, cancellationToken);

            if (department == null)
            {
                return Result<TenantStorageSummaryDto>.Failure("Phòng ban không tồn tại hoặc không thuộc tổ chức này.");
            }

            query = query.Where(f => f.DepartmentId == targetDepartmentId.Value || f.OwningDepartmentId == targetDepartmentId.Value);
        }

        if (request.FolderId.HasValue)
        {
            query = query.Where(f => f.FolderId == request.FolderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(f => f.Name.ToLower().Contains(term));
        }

        var files = await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FileEntryDto(
                f.Id,
                f.TenantId,
                f.OwnerId,
                f.Owner.FirstName + " " + f.Owner.LastName,
                f.FolderId,
                f.Name,
                f.StorageKey,
                f.ContentType,
                f.SizeBytes,
                f.Hash,
                f.ScanStatus,
                f.StorageProviderId,
                f.StorageProvider != null ? f.StorageProvider.Name : null,
                f.StorageProvider != null ? f.StorageProvider.Type.ToString() : null,
                f.CreatedAt,
                null,
                null
            ))
            .ToListAsync(cancellationToken);

        long quota = department != null ? department.StorageQuotaBytes : tenant.StorageQuotaBytes;
        long used = department != null ? department.StorageUsedBytes : tenant.StorageUsedBytes;
        long available = Math.Max(0, quota - used);
        double usagePercent = quota > 0 ? Math.Round((double)used / quota * 100, 2) : 0;

        var summary = new TenantStorageSummaryDto(
            tenant.Id,
            department != null ? $"{tenant.Name} - {department.Name}" : tenant.Name,
            quota,
            used,
            available,
            usagePercent,
            tenant.StorageProviderId,
            tenant.StorageProvider?.Name,
            tenant.StorageProvider?.Type.ToString(),
            files
        );

        return Result<TenantStorageSummaryDto>.Success(summary);
    }
}

