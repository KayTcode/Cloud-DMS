using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetAuditLogs;

public record GetAuditLogsQuery : IRequest<Result<PaginatedList<AuditLogDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
    public string? Action { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
}

public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, Result<PaginatedList<AuditLogDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetAuditLogsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PaginatedList<AuditLogDto>>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var callerRole = _currentUserService.Role;
        Guid? effectiveDepartmentId = request.DepartmentId;
        Guid? effectiveTenantId = request.TenantId;

        // BẢO MẬT & PHÂN QUYỀN CHỐNG IDOR/BOLA:
        // Nếu caller là DepartmentAdmin, bắt buộc chỉ được xem audit logs trong phạm vi phòng ban của mình
        if (callerRole == "DepartmentAdmin")
        {
            if (!_currentUserService.DepartmentId.HasValue || _currentUserService.DepartmentId.Value == Guid.Empty)
            {
                return Result<PaginatedList<AuditLogDto>>.Failure("Tài khoản chưa được liên kết với phòng ban nào.");
            }

            effectiveDepartmentId = _currentUserService.DepartmentId.Value;
            effectiveTenantId = _currentUserService.TenantId;
        }
        else if (callerRole == "TenantAdmin")
        {
            if (!_currentUserService.TenantId.HasValue || _currentUserService.TenantId.Value == Guid.Empty)
            {
                return Result<PaginatedList<AuditLogDto>>.Failure("Tài khoản chưa được liên kết với tổ chức/tenant nào.");
            }

            effectiveTenantId = _currentUserService.TenantId.Value;
        }
        else if (callerRole != "SystemAdmin" && _currentUserService.TenantId.HasValue)
        {
            effectiveTenantId = _currentUserService.TenantId.Value;
        }

        var query = _context.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
                .ThenInclude(u => u.Department)
            .Include(a => a.Tenant)
            .AsSplitQuery()
            .AsQueryable();

        if (effectiveTenantId.HasValue && effectiveTenantId.Value != Guid.Empty)
        {
            query = query.Where(a => a.TenantId == effectiveTenantId.Value);
        }

        if (effectiveDepartmentId.HasValue && effectiveDepartmentId.Value != Guid.Empty)
        {
            var deptId = effectiveDepartmentId.Value;
            query = query.Where(a =>
                (a.User != null && a.User.DepartmentId == deptId) ||
                ((a.EntityName == "User" || a.EntityName == "UserStorageQuota") && _context.Users.Any(u => u.Id == a.EntityId && u.DepartmentId == deptId)) ||
                (a.EntityName == "Department" && a.EntityId == deptId) ||
                (a.EntityName == "FileEntry" && _context.FileEntries.Any(f => f.Id == a.EntityId && (f.DepartmentId == deptId || f.OwningDepartmentId == deptId))));
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var act = request.Action.Trim();
            if (!act.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (act.Equals("upload", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => EF.Functions.Like(a.Action, "%Upload%"));
                }
                else if (act.Equals("download", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => EF.Functions.Like(a.Action, "%Download%") || EF.Functions.Like(a.Action, "%Read%"));
                }
                else if (act.Equals("delete", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => EF.Functions.Like(a.Action, "%Delete%") || EF.Functions.Like(a.Action, "%Purge%") || EF.Functions.Like(a.Action, "%Lifecycle%"));
                }
                else if (act.Equals("quota", StringComparison.OrdinalIgnoreCase) || act.Equals("quota_change", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => EF.Functions.Like(a.Action, "%Quota%"));
                }
                else if (act.Equals("member", StringComparison.OrdinalIgnoreCase) || act.Equals("member_invited", StringComparison.OrdinalIgnoreCase) || act.Equals("employee", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => EF.Functions.Like(a.Action, "%Employee%") || EF.Functions.Like(a.Action, "%User%"));
                }
                else
                {
                    query = query.Where(a => a.Action == act || EF.Functions.Like(a.Action, $"%{act}%"));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchPattern = $"%{request.SearchTerm.Trim()}%";
            query = query.Where(a => 
                EF.Functions.Like(a.Action, searchPattern) ||
                (a.Details != null && EF.Functions.Like(a.Details, searchPattern)) ||
                (a.User != null && a.User.FullName != null && EF.Functions.Like(a.User.FullName, searchPattern)) ||
                (a.User != null && EF.Functions.Like(a.User.Email, searchPattern)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rawItems = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                Timestamp = a.CreatedAt,
                a.Action,
                UserFullName = a.User != null ? a.User.FullName : null,
                UserEmail = a.User != null ? a.User.Email : null,
                DepartmentId = a.User != null ? a.User.DepartmentId : null,
                DepartmentName = (a.User != null && a.User.Department != null) ? a.User.Department.Name : null,
                a.IpAddress,
                a.Details,
                a.TenantId,
                TenantName = a.Tenant != null ? a.Tenant.Name : null
            })
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(a =>
        {
            var actor = !string.IsNullOrEmpty(a.UserFullName)
                ? a.UserFullName
                : (!string.IsNullOrEmpty(a.UserEmail) ? a.UserEmail : "System");

            var status = "SUCCESS";
            if (a.Action.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                (a.Details != null && (a.Details.Contains("thất bại", StringComparison.OrdinalIgnoreCase) || a.Details.Contains("fail", StringComparison.OrdinalIgnoreCase))))
            {
                status = "FAILED";
            }
            else if (a.Action.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                     (a.Details != null && (a.Details.Contains("cảnh báo", StringComparison.OrdinalIgnoreCase) || a.Details.Contains("warn", StringComparison.OrdinalIgnoreCase))))
            {
                status = "WARNING";
            }

            return new AuditLogDto
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                Action = a.Action,
                Actor = actor,
                ActorEmail = a.UserEmail,
                DepartmentId = a.DepartmentId,
                DepartmentName = a.DepartmentName,
                IpAddress = a.IpAddress ?? "Unknown",
                Status = status,
                Details = a.Details ?? string.Empty,
                TenantId = a.TenantId,
                TenantName = a.TenantName
            };
        }).ToList();

        var paginatedList = new PaginatedList<AuditLogDto>(items, totalCount, pageNumber, pageSize);
        return Result<PaginatedList<AuditLogDto>>.Success(paginatedList);
    }
}
