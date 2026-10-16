using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.DTOs;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetTenantFiles;

public class GetTenantFilesQueryHandler(
    IApplicationDbContext db, 
    IFileAccessContextResolver resolver,
    IFileAclQuery acl, 
    IFileAccessService access,
    ICurrentUserService currentUserService) : IRequestHandler<GetTenantFilesQuery, Result<TenantStorageSummaryDto>>
{
    public async Task<Result<TenantStorageSummaryDto>> Handle(GetTenantFilesQuery request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;

        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100
            || (long)(request.PageNumber - 1) * request.PageSize > int.MaxValue || request.SearchTerm?.Length > 1000)
            return Result<TenantStorageSummaryDto>.Failure("INVALID_LIST_REQUEST");

        var isSystemAdmin = currentUserService.IsAuthenticated && 
            (string.Equals(currentUserService.Role, "SystemAdmin", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(currentUserService.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        Guid effectiveTenantId;
        FileAccessContext? context = null;

        if (isSystemAdmin)
        {
            if (!request.TenantId.HasValue || request.TenantId.Value == Guid.Empty)
            {
                return Result<TenantStorageSummaryDto>.Failure("Vui lòng chỉ định TenantId.");
            }
            effectiveTenantId = request.TenantId.Value;
        }
        else
        {
            context = await resolver.ResolveContextAsync(ct);
            effectiveTenantId = context.TenantId;
            if (request.TenantId.HasValue && request.TenantId != effectiveTenantId)
            {
                throw new UnauthorizedAccessException();
            }
        }

        var tenant = await db.Tenants.AsNoTracking()
            .Include(t => t.StorageProvider)
            .SingleOrDefaultAsync(t => t.Id == effectiveTenantId, ct);

        if (tenant == null)
        {
            return Result<TenantStorageSummaryDto>.Failure("Tổ chức không tồn tại.");
        }

        var callerUserId = currentUserService.UserId ?? context?.Actor.UserId;
        var callerRoles = new List<string>();
        if (callerUserId.HasValue)
        {
            callerRoles = await db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == callerUserId.Value)
                .Select(ur => ur.Role.Name)
                .ToListAsync(ct);
        }

        var isTenantAdmin = isSystemAdmin || callerRoles.Contains("TenantAdmin") || callerRoles.Contains("SystemAdmin");
        var isDeptAdmin = callerRoles.Contains("DepartmentAdmin");

        var currentUser = callerUserId.HasValue
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerUserId.Value, ct)
            : null;

        Guid? targetDepartmentId = request.DepartmentId;
        if (!targetDepartmentId.HasValue && currentUser?.DepartmentId.HasValue == true && (isDeptAdmin || callerRoles.Contains("Employee")))
        {
            targetDepartmentId = currentUser.DepartmentId;
        }

        CleanArchCqrs.Domain.Entities.Department? department = null;
        if (targetDepartmentId.HasValue)
        {
            department = await db.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == targetDepartmentId.Value && d.TenantId == effectiveTenantId && d.IsActive, ct);

            if (department == null)
            {
                return Result<TenantStorageSummaryDto>.Failure("Phòng ban không tồn tại hoặc không thuộc tổ chức này.");
            }
        }

        IQueryable<FileEntry> scope;
        if (isTenantAdmin)
        {
            // TenantAdmin / SysAdmin có quyền quản trị và xem báo cáo toàn bộ tệp tin của Tổ chức
            scope = db.FileEntries.AsNoTracking()
                .Include(f => f.Owner)
                .Include(f => f.Department)
                .Where(f => f.TenantId == effectiveTenantId && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted);

            if (targetDepartmentId.HasValue)
            {
                scope = scope.Where(f => f.DepartmentId == targetDepartmentId.Value || f.OwningDepartmentId == targetDepartmentId.Value);
            }
        }
        else if (isDeptAdmin && targetDepartmentId.HasValue)
        {
            // DepartmentAdmin quản lý và xem toàn bộ tệp tin của phòng ban mình phụ trách
            scope = db.FileEntries.AsNoTracking()
                .Include(f => f.Owner)
                .Include(f => f.Department)
                .Where(f => f.TenantId == effectiveTenantId && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted
                    && (f.DepartmentId == targetDepartmentId.Value || f.OwningDepartmentId == targetDepartmentId.Value));
        }
        else
        {
            // Nhân viên thông thường chỉ xem tệp của mình hoặc tệp được chia sẻ qua ACL
            if (context == null) throw new UnauthorizedAccessException();
            scope = await acl.ReadScopeAsync(context, DateTimeOffset.UtcNow, ct);
            if (targetDepartmentId.HasValue)
            {
                scope = scope.Where(f => f.DepartmentId == targetDepartmentId.Value || f.OwningDepartmentId == targetDepartmentId.Value);
            }
        }

        if (request.FolderId.HasValue) 
            scope = scope.Where(f => f.FolderId == request.FolderId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = "%" + request.SearchTerm.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            scope = scope.Where(f => EF.Functions.Like(f.Name, pattern, "\\"));
        }

        var totalCount = await scope.CountAsync(ct);

        List<FileEntryDto> files;
        bool hasMore;

        var providerName = tenant.StorageProvider?.Name;
        var providerType = tenant.StorageProvider?.Type.ToString();

        if (isTenantAdmin || isDeptAdmin)
        {
            var pageRows = await scope.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize + 1)
                .ToListAsync(ct);

            hasMore = pageRows.Count > request.PageSize;
            var finalRows = pageRows.Take(request.PageSize).ToList();

            files = finalRows.Select(f => new FileEntryDto(
                f.Id, 
                f.TenantId, 
                f.OwnerId, 
                f.Owner != null && !string.IsNullOrWhiteSpace(f.Owner.FirstName) 
                    ? $"{f.Owner.FirstName} {f.Owner.LastName}".Trim() 
                    : (f.Owner != null ? f.Owner.Email : "Unknown"),
                f.FolderId, 
                f.Name, 
                f.StorageKey ?? "", 
                f.ContentType, 
                f.SizeBytes, 
                f.Hash, 
                f.ScanStatus ?? "Clean",
                f.StorageProviderId, 
                providerName, 
                providerType, 
                f.CreatedAt, 
                f.DepartmentId, 
                f.Department?.Name)).ToList();
        }
        else
        {
            if (context == null) throw new UnauthorizedAccessException();
            var rows = await scope.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
                .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize + 1)
                .Select(f => new { f.Id, f.DocumentVersion }).ToArrayAsync(ct);
            var grants = await access.CheckManyAsync(context, rows.Select(f => f.Id).ToArray(), ct);
            var ids = rows.Where(f => grants.TryGetValue(f.Id, out var grant) && grant.DocumentVersion == f.DocumentVersion)
                .Select(f => f.Id).ToArray();
            var pageRows = await scope.Include(f => f.Owner).Include(f => f.Department)
                .Where(f => ids.Contains(f.Id))
                .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
                .ToListAsync(ct);
            await access.AssertStillValidAsync(context, pageRows.Select(f => grants[f.Id]).ToArray(), ct);
            if (await resolver.ResolveContextAsync(ct) != context) throw new AuthorizationChangedException();
            hasMore = pageRows.Count > request.PageSize;
            var finalRows = pageRows.Take(request.PageSize).ToList();

            files = finalRows.Select(f => new FileEntryDto(
                f.Id, 
                f.TenantId, 
                f.OwnerId, 
                f.Owner != null && !string.IsNullOrWhiteSpace(f.Owner.FirstName) 
                    ? $"{f.Owner.FirstName} {f.Owner.LastName}".Trim() 
                    : (f.Owner != null ? f.Owner.Email : "Unknown"),
                f.FolderId, 
                f.Name, 
                f.StorageKey ?? "", 
                f.ContentType, 
                f.SizeBytes, 
                f.Hash, 
                f.ScanStatus ?? "Clean",
                f.StorageProviderId, 
                providerName, 
                providerType, 
                f.CreatedAt, 
                f.DepartmentId, 
                f.Department?.Name)).ToList();
        }

        var quota = department != null ? department.QuotaBytes : tenant.StorageQuotaBytes;
        var used = department != null ? department.UsedBytes : tenant.StorageUsedBytes;
        var summaryName = department != null ? $"{tenant.Name} - {department.Name}" : tenant.Name;

        return Result<TenantStorageSummaryDto>.Success(new(
            tenant.Id, 
            summaryName, 
            quota, 
            used,
            Math.Max(0, quota - used), 
            quota > 0 ? Math.Round((double)used / quota * 100, 2) : 0,
            tenant.StorageProviderId, 
            tenant.StorageProvider?.Name, 
            tenant.StorageProvider?.Type.ToString(),
            files, 
            totalCount, 
            request.PageNumber, 
            request.PageSize, 
            hasMore));
    }
}
