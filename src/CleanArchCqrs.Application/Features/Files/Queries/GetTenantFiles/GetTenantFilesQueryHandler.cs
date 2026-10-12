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
        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        Guid? tenantId;

        if (isSystemAdmin)
        {
            if (request.TenantId.HasValue)
            {
                tenantId = request.TenantId.Value;
            }
            else
            {
                return Result<TenantStorageSummaryDto>.Failure("Vui lòng chỉ định Tổ chức (Tenant) để xem danh sách tệp tin.");
            }
        }
        else
        {
            tenantId = _currentUserService.TenantId;
            if (!tenantId.HasValue)
            {
                var userId = _currentUserService.UserId;
                if (userId.HasValue)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
                    tenantId = user?.TenantId;
                }
            }
        }

        if (!tenantId.HasValue)
        {
            return Result<TenantStorageSummaryDto>.Failure("Không xác định được tổ chức/tenant.");
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

        // Với Employee: giới hạn chỉ thấy file thuộc phòng ban của mình
        if (_currentUserService.Role == "Employee" && _currentUserService.DepartmentId.HasValue)
        {
            query = query.Where(f => f.DepartmentId == _currentUserService.DepartmentId.Value);
        }

        if (request.FolderId.HasValue)
        {
            query = query.Where(f => f.FolderId == request.FolderId.Value);
        }

        // Tìm kiếm sargable theo tên file
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            query = query.Where(f => EF.Functions.Like(f.Name, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 100);

        var files = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
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
                f.DepartmentId,
                f.Department != null ? f.Department.Name : null
            ))
            .ToListAsync(cancellationToken);

        long quota = tenant.StorageQuotaBytes;
        long used = tenant.StorageUsedBytes;
        long available = Math.Max(0, quota - used);
        double usagePercent = quota > 0 ? Math.Round((double)used / quota * 100, 2) : 0;

        var summary = new TenantStorageSummaryDto(
            tenant.Id,
            tenant.Name,
            quota,
            used,
            available,
            usagePercent,
            tenant.StorageProviderId,
            tenant.StorageProvider?.Name,
            tenant.StorageProvider?.Type.ToString(),
            files,
            totalCount,
            pageNumber,
            pageSize
        );

        return Result<TenantStorageSummaryDto>.Success(summary);
    }
}
