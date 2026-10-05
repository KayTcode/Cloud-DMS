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
    private readonly IEmailService _emailService;

    public CreateUserCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IEmailService emailService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
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
            // 1. Sinh mã Tenant Code tự động từ tên
            var cleanLetters = new string(request.TenantName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var codeCandidate = string.IsNullOrWhiteSpace(cleanLetters)
                ? "T_" + Guid.NewGuid().ToString("N")[..6].ToUpper()
                : (cleanLetters.Length > 8 ? cleanLetters[..8] : cleanLetters);

            // Kiểm tra trùng lặp mã Tenant Code
            var codeExists = await _context.Tenants.AnyAsync(t => t.Code == codeCandidate, cancellationToken);
            if (codeExists)
            {
                codeCandidate = $"{codeCandidate[..Math.Min(5, codeCandidate.Length)]}_{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
            }

            // 2. Chưa gán dung lượng (đặt mặc định = 0 bytes)
            long quotaBytes = 0L;

            tenant = new Tenant(Guid.NewGuid(), request.TenantName.Trim(), codeCandidate, quotaBytes)
            {
                Description = !string.IsNullOrWhiteSpace(request.Plan)
                    ? $"{request.Plan} Subscription Tenant Organization"
                    : "Tenant Organization"
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
        // Đúng khi thảo mãn điều kiện có tên tenant mới và để trống mật khẩu 
        var isInvitation = !string.IsNullOrWhiteSpace(request.TenantName) || string.IsNullOrEmpty(request.Password);
        // Nếu để trống sinh ra chuỗi ngẫu nhiên 32 ký tự , còn nếu không trống thì dùng luôn 
        var initialPassword = string.IsNullOrEmpty(request.Password) ? Guid.NewGuid().ToString("N") : request.Password;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.HashPassword(initialPassword),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            TenantId = effectiveTenantId,
            Tenant = tenant,
            DepartmentId = request.DepartmentId,
            Department = department,
            IsActive = !isInvitation,
            EmailConfirmed = !isInvitation
        };

        // Lấy role tenantAdmin 
        var tenantAdminRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "TenantAdmin", cancellationToken);

        // Gán role 
        if (tenantAdminRole != null)
        {
            user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, tenantAdminRole.Id));
        }

        await _context.Users.AddAsync(user, cancellationToken);

        // 5. If invitation, generate token and send activation email
        if (isInvitation)
        {
            var tokenString = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var invitationToken = new EmailVerificationToken(
                id: Guid.NewGuid(),
                userId: user.Id,
                token: tokenString,
                tokenType: "TenantAdminInvitation",
                expirationHours: 48);

            await _context.EmailVerificationTokens.AddAsync(invitationToken, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Send Email Invitation
            await _emailService.SendTenantAdminInvitationAsync(
                toEmail: user.Email,
                recipientName: user.FullName,
                tenantName: tenant?.Name ?? "Tổ chức",
                token: tokenString,
                cancellationToken: cancellationToken);
        }
        else
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

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
