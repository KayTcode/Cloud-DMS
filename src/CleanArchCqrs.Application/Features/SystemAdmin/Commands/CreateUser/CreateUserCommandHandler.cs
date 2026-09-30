using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.CreateUser;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public CreateUserCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<UserDto>> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Check duplicate email
        var emailExists = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Result<UserDto>.Failure($"A user with email '{request.Email}' already exists.");
        }

        // 2. Handle Tenant Provisioning or Validation
        Tenant? tenant = null;
        Department? department = null;

        if (!string.IsNullOrWhiteSpace(request.TenantName))
        {
            // Calculate storage quota based on selected plan
            var planName = request.Plan?.Trim().ToLowerInvariant() ?? "enterprise";
            var quotaBytes = planName switch
            {
                "starter" => 10L * 1024 * 1024 * 1024,       // 10 GB
                "business" => 50L * 1024 * 1024 * 1024,     // 50 GB
                _ => 100L * 1024 * 1024 * 1024              // 100 GB (Enterprise)
            };

            // Generate a clean tenant code from the name
            var cleanLetters = new string(request.TenantName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var codeCandidate = string.IsNullOrWhiteSpace(cleanLetters) 
                ? "T_" + Guid.NewGuid().ToString("N")[..6].ToUpper() 
                : (cleanLetters.Length > 8 ? cleanLetters[..8] : cleanLetters);

            // Ensure unique code
            var codeExists = await _context.Tenants.AnyAsync(t => t.Code == codeCandidate, cancellationToken);
            if (codeExists)
            {
                codeCandidate = $"{codeCandidate[..Math.Min(5, codeCandidate.Length)]}_{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
            }

            tenant = new Tenant(Guid.NewGuid(), request.TenantName.Trim(), codeCandidate, quotaBytes)
            {
                Description = $"{request.Plan ?? "Enterprise"} Subscription Tenant Organization"
            };

            await _context.Tenants.AddAsync(tenant, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (request.TenantId.HasValue)
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

            var currentTenantId = tenant?.Id ?? request.TenantId;
            if (currentTenantId.HasValue && department.TenantId != currentTenantId.Value)
            {
                return Result<UserDto>.Failure($"Department '{department.Name}' does not belong to Tenant '{tenant?.Name}'.");
            }
        }

        // 3. Create User entity with TenantAdmin role
        var effectiveTenantId = tenant?.Id ?? request.TenantId;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            TenantId = effectiveTenantId,
            Tenant = tenant,
            DepartmentId = request.DepartmentId,
            Department = department,
            IsActive = true
        };

        // 4. Enforce TenantAdmin Role for System Admin tenant creation
        var tenantAdminRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "TenantAdmin", cancellationToken);

        if (tenantAdminRole != null)
        {
            user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, tenantAdminRole.Id));
        }

        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Fetch back with eager loaded relations for response
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
