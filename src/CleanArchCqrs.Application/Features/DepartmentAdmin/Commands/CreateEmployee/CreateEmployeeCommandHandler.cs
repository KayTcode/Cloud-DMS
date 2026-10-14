using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.CreateEmployee;

public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUserService;

    public CreateEmployeeCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IEmailService emailService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<UserDto>> Handle(
        CreateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        // 1. Kiểm tra quyền của người gọi: DepartmentAdmin của phòng hoặc TenantAdmin của tổ chức
        var callerRole = _currentUserService.Role;
        var callerTenantId = _currentUserService.TenantId;
        var callerDeptId = _currentUserService.DepartmentId;

        var department = await _context.Departments
            .Include(d => d.Tenant)
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<UserDto>.Failure($"Phòng ban với ID '{request.DepartmentId}' không tồn tại.");
        }

        if (!department.IsActive)
        {
            return Result<UserDto>.Failure($"Phòng ban '{department.Name}' hiện đang bị vô hiệu hóa.");
        }

        if (callerRole == "DepartmentAdmin")
        {
            if (!callerDeptId.HasValue && _currentUserService.UserId.HasValue)
            {
                callerDeptId = await _context.Users
                    .Where(u => u.Id == _currentUserService.UserId.Value)
                    .Select(u => u.DepartmentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!callerDeptId.HasValue || callerDeptId.Value != department.Id)
            {
                return Result<UserDto>.Failure("Bạn chỉ có quyền tạo nhân viên trong phòng ban của mình.");
            }
        }
        else if (callerRole == "TenantAdmin")
        {
            if (!callerTenantId.HasValue || callerTenantId.Value != department.TenantId)
            {
                return Result<UserDto>.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }
        }
        else if (callerRole != "SystemAdmin")
        {
            return Result<UserDto>.Failure("Bạn không có quyền tạo nhân viên cho phòng ban.");
        }

        // 2. Kiểm tra trùng lặp email
        var emailExists = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Result<UserDto>.Failure($"Email '{request.Email}' đã tồn tại trong hệ thống.");
        }

        // 3. Tính toán và kiểm tra Quota cấp cho nhân viên (trích từ Quota khả dụng của phòng ban)
        long quotaToAllocate = request.StorageQuotaBytes ?? 0L; // Mặc định 0 B nếu chưa chỉ định

        if (quotaToAllocate < 0)
        {
            return Result<UserDto>.Failure("Hạn mức lưu trữ không được là số âm.");
        }

        if (quotaToAllocate > 0)
        {
            var totalAllocatedInDept = await _context.UserStorageQuotas
                .Where(q => q.User.DepartmentId == department.Id)
                .SumAsync(q => q.QuotaBytes, cancellationToken);

            var availableInDept = Math.Max(0, department.StorageQuotaBytes - totalAllocatedInDept);

            if (quotaToAllocate > availableInDept)
            {
                return Result<UserDto>.Failure(
                    $"Hạn mức yêu cầu ({FormatBytes(quotaToAllocate)}) vượt quá dung lượng khả dụng của phòng ban (Phòng còn trống: {FormatBytes(availableInDept)}). Hãy cấp thêm dung lượng cho phòng ban trước.");
            }
        }

        // 4. Tìm vai trò 'Employee'
        var employeeRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "Employee", cancellationToken);

        if (employeeRole == null)
        {
            return Result<UserDto>.Failure("Vai trò 'Employee' chưa được định nghĩa trong hệ thống.");
        }

        // 5. Khởi tạo tài khoản
        var isInvitation = string.IsNullOrEmpty(request.Password);
        var initialPassword = isInvitation ? Guid.NewGuid().ToString("N") : request.Password!;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = (request.Email ?? string.Empty).Trim(),
            PasswordHash = _passwordHasher.HashPassword(initialPassword),
            FirstName = (request.FirstName ?? string.Empty).Trim(),
            LastName = (request.LastName ?? string.Empty).Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            TenantId = department.TenantId,
            DepartmentId = department.Id,
            IsActive = !isInvitation,
            EmailConfirmed = !isInvitation
        };

        user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, employeeRole.Id));
        await _context.Users.AddAsync(user, cancellationToken);

        // 6. Cấp UserStorageQuota cho Employee
        var userQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            QuotaBytes = quotaToAllocate,
            UsedBytes = 0
        };
        await _context.UserStorageQuotas.AddAsync(userQuota, cancellationToken);

        // 7. Tạo Token nếu là thư mời
        string? tokenString = null;
        if (isInvitation)
        {
            tokenString = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var invitationToken = new EmailVerificationToken(
                id: Guid.NewGuid(),
                userId: user.Id,
                token: tokenString,
                tokenType: "EmployeeInvitation",
                expirationHours: 48);

            await _context.EmailVerificationTokens.AddAsync(invitationToken, cancellationToken);
        }

        // 8. AuditLog
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = department.TenantId,
            UserId = _currentUserService.UserId,
            Action = isInvitation ? "InviteEmployee" : "CreateEmployee",
            EntityName = "User",
            EntityId = user.Id,
            Details = $"Tạo tài khoản nhân viên '{user.FullName}' trong phòng '{department.Name}'. Hạn mức: {FormatBytes(quotaToAllocate)}.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);

        if (isInvitation && !string.IsNullOrEmpty(tokenString))
        {
            try
            {
                await _emailService.SendUserActivationEmailAsync(
                    toEmail: user.Email,
                    recipientName: user.FullName,
                    token: tokenString,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                // Không rollback nếu gửi mail lỗi, token vẫn lưu trong DB
            }
        }

        var createdUser = await _context.Users
            .AsNoTracking()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);

        return Result<UserDto>.Success(UserDto.FromEntity(createdUser ?? user));
    }
}
