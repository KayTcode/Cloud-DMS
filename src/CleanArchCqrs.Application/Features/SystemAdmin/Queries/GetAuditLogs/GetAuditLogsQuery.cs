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
}

public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, Result<PaginatedList<AuditLogDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetAuditLogsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PaginatedList<AuditLogDto>>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _context.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Tenant)
            .AsSplitQuery()
            .AsQueryable();

        if (request.TenantId.HasValue && request.TenantId.Value != Guid.Empty)
        {
            query = query.Where(a => a.TenantId == request.TenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            query = query.Where(a => a.Action == request.Action.Trim());
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
