using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.TenantAdmin.Commands.CreateDepartmentAdmin;

public class CreateDepartmentAdminCommandHandler : IRequestHandler<CreateDepartmentAdminCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public CreateDepartmentAdminCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<UserDto>> Handle(
        CreateDepartmentAdminCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Check duplicate email across system
        var emailExists = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Result<UserDto>.Failure($"A user with email '{request.Email}' already exists.");
        }

        // 2. Validate Tenant exists and is active
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);

        if (tenant == null)
        {
            return Result<UserDto>.Failure($"Tenant with ID '{request.TenantId}' does not exist.");
        }

        if (!tenant.IsActive)
        {
            return Result<UserDto>.Failure($"Tenant '{tenant.Name}' is inactive.");
        }

        // 3. Validate Department if provided (optional)
        Department? department = null;
        if (request.DepartmentId.HasValue && request.DepartmentId.Value != Guid.Empty)
        {
            department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, cancellationToken);

            if (department == null)
            {
                return Result<UserDto>.Failure($"Department with ID '{request.DepartmentId}' does not exist.");
            }

            if (department.TenantId != request.TenantId)
            {
                return Result<UserDto>.Failure($"Department '{department.Name}' does not belong to Tenant '{tenant.Name}'.");
            }

            if (!department.IsActive)
            {
                return Result<UserDto>.Failure($"Department '{department.Name}' is inactive.");
            }
        }

        // 4. Find DepartmentAdmin role (prefer tenant-specific role if exists, otherwise fallback to global system role)
        var departmentAdminRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "DepartmentAdmin" && (r.TenantId == request.TenantId || r.TenantId == null), cancellationToken);

        if (departmentAdminRole == null)
        {
            return Result<UserDto>.Failure("DepartmentAdmin role is not defined in the system.");
        }

        // 5. Create User entity with Role=DepartmentAdmin
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            TenantId = request.TenantId,
            Tenant = tenant,
            DepartmentId = department?.Id,
            Department = department,
            IsActive = true
        };

        // 6. Assign DepartmentAdmin role
        user.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = departmentAdminRole.Id,
            User = user,
            Role = departmentAdminRole
        });

        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // 7. Load navigation properties for returning DTO
        var createdUser = await _context.Users
            .AsNoTracking()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);

        return Result<UserDto>.Success(UserDto.FromEntity(createdUser ?? user));
    }
}
