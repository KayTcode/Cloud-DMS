using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetTenantFiles;

public class GetTenantFilesQueryHandler(IApplicationDbContext db, IFileAccessContextResolver resolver,
    IFileAclQuery acl, IFileAccessService access) : IRequestHandler<GetTenantFilesQuery, Result<TenantStorageSummaryDto>>
{
    public async Task<Result<TenantStorageSummaryDto>> Handle(GetTenantFilesQuery request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100
            || (long)(request.PageNumber - 1) * request.PageSize > int.MaxValue || request.SearchTerm?.Length > 1000)
            return Result<TenantStorageSummaryDto>.Failure("INVALID_LIST_REQUEST");
        var context = await resolver.ResolveContextAsync(ct);
        if (request.TenantId.HasValue && request.TenantId != context.TenantId)
            throw new UnauthorizedAccessException();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.StorageProvider)
            .SingleAsync(t => t.Id == context.TenantId, ct);
        var scope = await acl.ReadScopeAsync(context, DateTimeOffset.UtcNow, ct);
        if (request.FolderId.HasValue) scope = scope.Where(f => f.FolderId == request.FolderId);
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = "%" + request.SearchTerm.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            scope = scope.Where(f => EF.Functions.Like(f.Name, pattern, "\\"));
        }

        var currentUser = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == context.Actor.UserId, ct);
        Guid? targetDepartmentId = request.DepartmentId;
        if (!targetDepartmentId.HasValue && currentUser?.DepartmentId.HasValue == true)
        {
            var isDeptScoped = await db.UserRoles.AnyAsync(ur => ur.UserId == context.Actor.UserId && (ur.Role.Name == "DepartmentAdmin" || ur.Role.Name == "Employee"), ct);
            if (isDeptScoped)
            {
                targetDepartmentId = currentUser.DepartmentId;
            }
        }

        CleanArchCqrs.Domain.Entities.Department? department = null;
        if (targetDepartmentId.HasValue)
        {
            department = await db.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == targetDepartmentId.Value && d.TenantId == context.TenantId && d.IsActive, ct);

            if (department == null)
            {
                return Result<TenantStorageSummaryDto>.Failure("Phòng ban không tồn tại hoặc không thuộc tổ chức này.");
            }

            scope = scope.Where(f => f.DepartmentId == targetDepartmentId.Value || f.OwningDepartmentId == targetDepartmentId.Value);
        }

        var rows = await scope.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize + 1)
            .Select(f => new { f.Id, f.DocumentVersion }).ToArrayAsync(ct);
        var grants = await access.CheckManyAsync(context, rows.Select(f => f.Id).ToArray(), ct);
        var ids = rows.Where(f => grants.TryGetValue(f.Id, out var grant) && grant.DocumentVersion == f.DocumentVersion)
            .Select(f => f.Id).ToArray();
        var files = await scope.Where(f => ids.Contains(f.Id)).OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
            .Select(f => new FileEntryDto(f.Id, f.TenantId, f.OwnerId, f.Owner.FirstName + " " + f.Owner.LastName,
                f.FolderId, f.Name, "", f.ContentType, f.SizeBytes, null, f.ScanStatus,
                null, null, null, f.CreatedAt, f.DepartmentId, f.Department != null ? f.Department.Name : null))
            .ToListAsync(ct);
        await access.AssertStillValidAsync(context, files.Select(f => grants[f.Id]).ToArray(), ct);
        if (await resolver.ResolveContextAsync(ct) != context) throw new AuthorizationChangedException();
        var quota = department != null ? department.QuotaBytes : tenant.StorageQuotaBytes;
        var used = department != null ? department.UsedBytes : tenant.StorageUsedBytes;
        var summaryName = department != null ? $"{tenant.Name} - {department.Name}" : tenant.Name;
        var hasMore = files.Count > request.PageSize;
        files = files.Take(request.PageSize).ToList();
        // Preserve DTO shape, but counts are only this authorized page, never hidden candidates.
        return Result<TenantStorageSummaryDto>.Success(new(tenant.Id, summaryName, quota, used,
            Math.Max(0, quota - used), quota > 0 ? Math.Round((double)used / quota * 100, 2) : 0,
            tenant.StorageProviderId, tenant.StorageProvider?.Name, tenant.StorageProvider?.Type.ToString(),
            files, files.Count, request.PageNumber, request.PageSize, hasMore));
    }
}
