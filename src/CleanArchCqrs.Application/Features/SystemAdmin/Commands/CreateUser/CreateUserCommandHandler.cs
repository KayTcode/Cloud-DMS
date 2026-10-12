using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
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

            // 2. Xử lý gán Storage Provider & Quota ngay khi tạo Tenant (nếu SystemAdmin chọn)
            Guid? assignedProviderId = null;
            long assignedQuotaBytes = 0L;

            if (request.StorageProviderId.HasValue && request.StorageProviderId.Value != Guid.Empty)
            {
                var provider = await _context.StorageProviders
                    .Include(sp => sp.Tenants)
                    .FirstOrDefaultAsync(sp => sp.Id == request.StorageProviderId.Value, cancellationToken);

                if (provider == null)
                {
                    return Result<UserDto>.Failure($"Không tìm thấy Cloud Storage Provider với ID '{request.StorageProviderId}'.");
                }

                if (!provider.IsActive)
                {
                    return Result<UserDto>.Failure($"Cloud Storage Provider '{provider.Name}' hiện đang bị vô hiệu hóa.");
                }

                var totalCapacity = provider.TotalCapacityBytes > 0 ? provider.TotalCapacityBytes : (15L * 1024 * 1024 * 1024);
                var cloudFree = Math.Max(0, totalCapacity - provider.UsedCapacityBytes);
                var allocatedToOthers = provider.Tenants.Sum(t => t.StorageQuotaBytes);
                var availableForThisTenant = Math.Max(0, cloudFree - allocatedToOthers);

                long requestedQuota = request.StorageQuotaBytes ?? (10L * 1024 * 1024 * 1024); // Mặc định 10 GB nếu có chọn provider
                if (requestedQuota > availableForThisTenant)
                {
                    return Result<UserDto>.Failure(
                        $"Vượt quá dung lượng khả dụng của Cloud Provider '{provider.Name}'! Bạn yêu cầu {FormatBytes(requestedQuota)} nhưng Cloud chỉ còn trống {FormatBytes(availableForThisTenant)}.");
                }

                assignedProviderId = provider.Id;
                assignedQuotaBytes = requestedQuota;
            }

            tenant = new Tenant(Guid.NewGuid(), request.TenantName.Trim(), codeCandidate, assignedQuotaBytes)
            {
                StorageProviderId = assignedProviderId,
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

        // 3. Create User entity (TenantAdmin - không gắn DepartmentId)
        var effectiveTenantId = tenant?.Id ?? request.TenantId;
        var isInvitation = !string.IsNullOrWhiteSpace(request.TenantName) || string.IsNullOrEmpty(request.Password);
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
            DepartmentId = null, // SystemAdmin chỉ tạo TenantAdmin, không thuộc bất kỳ phòng ban nào
            Department = null,
            IsActive = !isInvitation,
            EmailConfirmed = !isInvitation
        };

        // Gán Role: Ưu tiên danh sách RoleIds client gửi lên, nếu trống thì mặc định là TenantAdmin
        if (request.RoleIds != null && request.RoleIds.Any())
        {
            var roles = await _context.Roles
                .Where(r => request.RoleIds.Contains(r.Id))
                .ToListAsync(cancellationToken);

            foreach (var r in roles)
            {
                user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, r.Id));
            }
        }
        else
        {
            var tenantAdminRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.Name == "TenantAdmin", cancellationToken);

            if (tenantAdminRole != null)
            {
                user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, tenantAdminRole.Id));
            }
        }

        await _context.Users.AddAsync(user, cancellationToken);

        // Khởi tạo ví hạn mức UserStorageQuota (mặc định = 0 để TenantAdmin quản lý tổng thể, không chiếm Quota)
        var userQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DepartmentId = null,
            QuotaBytes = 0L,
            UsedBytes = 0
        };
        await _context.UserStorageQuotas.AddAsync(userQuota, cancellationToken);

        // 5. If invitation, generate token and send activation email
        if (isInvitation)
        {
            // Hệ thống sinh token ngẫu nhiên gồm 2 chuỗi nối lại với nhau 2 x 32 ký tự = 64 ký tự 
            var tokenString = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            // Tạo EmailVerificationToken để lưu vào database 
            var invitationToken = new EmailVerificationToken(
                id: Guid.NewGuid(),
                userId: user.Id,
                token: tokenString,
                tokenType: "TenantAdminInvitation",
                expirationHours: 48);

            await _context.EmailVerificationTokens.AddAsync(invitationToken, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Send Email Invitation
            try
            {
                await _emailService.SendTenantAdminInvitationAsync(
                    toEmail: user.Email,
                    recipientName: user.FullName,
                    tenantName: tenant?.Name ?? "Tổ chức",
                    token: tokenString,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                // Không làm gián đoạn luồng tạo tài khoản nếu SMTP gặp sự cố ngoại tuyến
            }
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
