using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentMembers;

public record GetDepartmentMembersWithPaginationQuery : IRequest<Result<PaginatedList<DepartmentMemberDto>>>
{
    public Guid DepartmentId { get; init; }
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public class GetDepartmentMembersWithPaginationQueryHandler
    : IRequestHandler<GetDepartmentMembersWithPaginationQuery, Result<PaginatedList<DepartmentMemberDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetDepartmentMembersWithPaginationQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PaginatedList<DepartmentMemberDto>>> Handle(
        GetDepartmentMembersWithPaginationQuery request,
        CancellationToken cancellationToken)
    {
        var department = await _context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<PaginatedList<DepartmentMemberDto>>.Failure($"Phòng ban với ID '{request.DepartmentId}' không tồn tại.");
        }

        // Kiểm tra phân quyền truy cập phòng ban
        var callerRole = _currentUserService.Role;
        var callerDeptId = _currentUserService.DepartmentId;
        var callerTenantId = _currentUserService.TenantId;

        if (callerRole == "DepartmentAdmin")
        {
            if (!callerDeptId.HasValue && _currentUserService.UserId.HasValue)
            {
                var caller = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);
                callerDeptId = caller?.DepartmentId;
            }

            if (!callerDeptId.HasValue || callerDeptId.Value != request.DepartmentId)
            {
                return Result<PaginatedList<DepartmentMemberDto>>.Failure("Bạn chỉ có quyền xem danh sách thành viên trong phòng ban của mình.");
            }
        }
        else if (callerRole == "TenantAdmin")
        {
            if (!callerTenantId.HasValue && _currentUserService.UserId.HasValue)
            {
                var caller = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);
                callerTenantId = caller?.TenantId;
            }

            if (!callerTenantId.HasValue || callerTenantId.Value != department.TenantId)
            {
                return Result<PaginatedList<DepartmentMemberDto>>.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }
        }
        else if (callerRole != "SystemAdmin")
        {
            return Result<PaginatedList<DepartmentMemberDto>>.Failure("Bạn không có quyền xem thành viên của phòng ban này.");
        }

        var query = _context.Users
            .AsNoTracking()
            .AsSplitQuery()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.DepartmentId == request.DepartmentId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            query = query.Where(u =>
                EF.Functions.Like(u.FirstName, pattern) ||
                EF.Functions.Like(u.LastName, pattern) ||
                EF.Functions.Like(u.Email, pattern) ||
                (u.PhoneNumber != null && EF.Functions.Like(u.PhoneNumber, pattern)));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(u => u.IsActive == request.IsActive.Value);
        }

        query = query.OrderByDescending(u => u.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 10 : request.PageSize, 1, 100);

        var users = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToList();
        var userQuotas = await _context.UserStorageQuotas
            .AsNoTracking()
            .Where(q => userIds.Contains(q.UserId))
            .ToDictionaryAsync(q => q.UserId, cancellationToken);

        var dtoList = users.Select(u =>
        {
            userQuotas.TryGetValue(u.Id, out var quota);
            return DepartmentMemberDto.Create(u, quota);
        }).ToList();

        var paginatedResult = new PaginatedList<DepartmentMemberDto>(dtoList, totalCount, pageNumber, pageSize);
        return Result<PaginatedList<DepartmentMemberDto>>.Success(paginatedResult);
    }
}
