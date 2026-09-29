using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.AssignUserRoles;

public class AssignUserRolesCommandHandler : IRequestHandler<AssignUserRolesCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;

    public AssignUserRolesCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<UserDto>> Handle(
        AssignUserRolesCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

        if (user == null)
        {
            return Result<UserDto>.Failure($"User with ID '{request.Id}' was not found.");
        }

        // 1. Validate Tenant & Department if provided
        Tenant? tenant = null;
        Department? department = null;

        if (request.TenantId.HasValue)
        {
            tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == request.TenantId.Value, cancellationToken);

            if (tenant == null)
            {
                return Result<UserDto>.Failure($"Tenant with ID '{request.TenantId}' does not exist.");
            }
        }

        if (request.DepartmentId.HasValue)
        {
            department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, cancellationToken);

            if (department == null)
            {
                return Result<UserDto>.Failure($"Department with ID '{request.DepartmentId}' does not exist.");
            }

            if (request.TenantId.HasValue && department.TenantId != request.TenantId.Value)
            {
                return Result<UserDto>.Failure($"Department '{department.Name}' does not belong to Tenant '{tenant?.Name}'.");
            }
        }

        user.TenantId = request.TenantId;
        user.Tenant = tenant;
        user.DepartmentId = request.DepartmentId;
        user.Department = department;

        // 2. Update Roles
        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var targetRoleIds = request.RoleIds ?? new List<Guid>();

        // Remove unassigned roles
        var rolesToRemove = user.UserRoles.Where(ur => !targetRoleIds.Contains(ur.RoleId)).ToList();
        foreach (var userRole in rolesToRemove)
        {
            _context.UserRoles.Remove(userRole);
        }

        // Add newly assigned roles
        var rolesToAdd = targetRoleIds.Except(currentRoleIds).ToList();
        if (rolesToAdd.Any())
        {
            var validRoles = await _context.Roles
                .Where(r => rolesToAdd.Contains(r.Id))
                .ToListAsync(cancellationToken);

            foreach (var role in validRoles)
            {
                _context.UserRoles.Add(new UserRole
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = role.Id,
                    User = user,
                    Role = role
                });
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        // Fetch back with eager loaded relations for response
        var updatedUser = await _context.Users
            .AsNoTracking()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);

        return Result<UserDto>.Success(UserDto.FromEntity(updatedUser ?? user));
    }
}
