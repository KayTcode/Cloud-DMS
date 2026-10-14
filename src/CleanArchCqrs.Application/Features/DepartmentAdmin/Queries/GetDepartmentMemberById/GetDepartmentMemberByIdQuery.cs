using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentMemberById;

public record GetDepartmentMemberByIdQuery(Guid DepartmentId, Guid UserId) : IRequest<Result<DepartmentMemberDto>>;

public class GetDepartmentMemberByIdQueryHandler
    : IRequestHandler<GetDepartmentMemberByIdQuery, Result<DepartmentMemberDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetDepartmentMemberByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DepartmentMemberDto>> Handle(
        GetDepartmentMemberByIdQuery request,
        CancellationToken cancellationToken)
    {
        var department = await _context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<DepartmentMemberDto>.Failure($"Phòng ban với ID '{request.DepartmentId}' không tồn tại.");
        }

        // Kiểm tra quyền
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
                return Result<DepartmentMemberDto>.Failure("Bạn chỉ có quyền xem thành viên trong phòng ban của mình.");
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
                return Result<DepartmentMemberDto>.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }
        }
        else if (callerRole != "SystemAdmin")
        {
            return Result<DepartmentMemberDto>.Failure("Bạn không có quyền truy cập thông tin thành viên phòng ban này.");
        }

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.DepartmentId == request.DepartmentId, cancellationToken);

        if (user == null)
        {
            return Result<DepartmentMemberDto>.Failure($"Không tìm thấy thành viên với ID '{request.UserId}' trong phòng ban này.");
        }

        var quota = await _context.UserStorageQuotas
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.UserId == user.Id, cancellationToken);

        return Result<DepartmentMemberDto>.Success(DepartmentMemberDto.Create(user, quota));
    }
}
