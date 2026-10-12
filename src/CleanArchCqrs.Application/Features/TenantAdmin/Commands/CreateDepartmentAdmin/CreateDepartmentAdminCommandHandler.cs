using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.TenantAdmin.Commands.CreateDepartmentAdmin;

/// <summary>
/// Handler xử lý tạo tài khoản Trưởng phòng (Department Admin) bởi Quản trị viên tổ chức (Tenant Admin).
/// Hỗ trợ cả 2 mô hình:
/// 1. Gán ngay vào phòng ban đã có sẵn (nếu truyền DepartmentId).
/// 2. Cấp trước một vùng nhớ (Quota) để Trưởng phòng tự thiết lập phòng ban sau (Self-Setup).
/// Đồng thời hỗ trợ gửi Email kích hoạt tài khoản qua Gmail (nếu mật khẩu để trống).
/// </summary>
public class CreateDepartmentAdminCommandHandler : IRequestHandler<CreateDepartmentAdminCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;

    public CreateDepartmentAdminCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IEmailService emailService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
    }

    public async Task<Result<UserDto>> Handle(
        CreateDepartmentAdminCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // =========================================================================
        // BƯỚC 1: KIỂM TRA TRÙNG LẶP EMAIL TRÊN TOÀN HỆ THỐNG
        // Mỗi tài khoản trong hệ thống phải có email là duy nhất (không phân biệt hoa thường).
        // =========================================================================
        var emailExists = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Result<UserDto>.Failure($"Email '{request.Email}' đã tồn tại trong hệ thống.");
        }

        // =========================================================================
        // BƯỚC 2: XÁC THỰC TỔ CHỨC (TENANT)
        // Kiểm tra Tenant có tồn tại trong CSDL và đang ở trạng thái hoạt động (Active) hay không.
        // =========================================================================
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);

        if (tenant == null)
        {
            return Result<UserDto>.Failure($"Tổ chức (Tenant) với ID '{request.TenantId}' không tồn tại.");
        }

        if (!tenant.IsActive)
        {
            return Result<UserDto>.Failure($"Tổ chức '{tenant.Name}' hiện đang bị vô hiệu hóa.");
        }

        // =========================================================================
        // BƯỚC 3: KIỂM TRA PHÒNG BAN (NẾU CÓ TRUYỀN)
        // - Không bắt buộc truyền: Trưởng phòng có thể tự tạo phòng ban sau khi đăng nhập.
        // - Nếu có truyền: Kiểm tra phòng ban phải thuộc tổ chức này và đang hoạt động.
        // =========================================================================
        Department? department = null;
        if (request.DepartmentId.HasValue && request.DepartmentId.Value != Guid.Empty)
        {
            department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value && d.TenantId == tenant.Id, cancellationToken);

            if (department == null)
            {
                return Result<UserDto>.Failure($"Phòng ban với ID '{request.DepartmentId}' không tồn tại trong tổ chức.");
            }

            if (!department.IsActive)
            {
                return Result<UserDto>.Failure($"Phòng ban '{department.Name}' hiện đang bị vô hiệu hóa.");
            }
        }

        // =========================================================================
        // BƯỚC 4: KIỂM TRA TRƯỞNG PHÒNG ĐÃ TỒN TẠI & TÍNH TOÁN QUOTA (P2-05 / BUG-11)
        // =========================================================================
        long personalQuotaBytes = 0L;

        if (department != null)
        {
            // 4.1. Kiểm tra xem phòng ban đã có Trưởng phòng chưa
            var existingDeptAdmin = await _context.Users
                .AnyAsync(u => u.DepartmentId == department.Id 
                            && u.UserRoles.Any(ur => ur.Role.Name == "DepartmentAdmin"), cancellationToken);
            if (existingDeptAdmin)
            {
                return Result<UserDto>.Failure($"Phòng ban '{department.Name}' hiện đã có Trưởng phòng quản lý. Mỗi phòng ban chỉ được bổ nhiệm một Trưởng phòng.");
            }

            // 4.2. Không ghi đè Quota phòng ban (đã do AllocateDepartmentStorageQuotaCommand quản lý riêng).
            // Nếu có yêu cầu cấp quota cá nhân ban đầu cho Trưởng phòng, kiểm tra trần Quota của chính phòng ban đó.
            if (request.StorageQuotaBytes.HasValue && request.StorageQuotaBytes.Value > 0)
            {
                var usersAllocatedInDept = await _context.UserStorageQuotas
                    .Where(q => q.User.DepartmentId == department.Id)
                    .SumAsync(q => q.QuotaBytes, cancellationToken);

                var availableInDept = Math.Max(0, department.StorageQuotaBytes - usersAllocatedInDept);
                if (request.StorageQuotaBytes.Value > availableInDept)
                {
                    return Result<UserDto>.Failure(
                        $"Hạn mức cá nhân yêu cầu ({FormatBytes(request.StorageQuotaBytes.Value)}) vượt quá dung lượng khả dụng của phòng ban '{department.Name}' (Phòng còn trống: {FormatBytes(availableInDept)}).");
                }
                personalQuotaBytes = request.StorageQuotaBytes.Value;
            }
        }
        else
        {
            // 4.3. Mô hình Self-Setup: Trưởng phòng chưa có phòng ban.
            // Cấp một lượng Quota từ Tenant để Trưởng phòng tạm giữ và đem sang thiết lập phòng ban sau (SetupDepartmentCommand).
            long quotaToAllocate = request.StorageQuotaBytes ?? 0L; // Mặc định 0 B nếu chưa chỉ định

            if (quotaToAllocate > 0)
            {
                var existingDeptsQuota = await _context.Departments
                    .Where(d => d.TenantId == tenant.Id)
                    .SumAsync(d => d.StorageQuotaBytes, cancellationToken);

                var pendingAdminsQuota = await _context.UserStorageQuotas
                    .Where(q => q.User.TenantId == tenant.Id && q.User.DepartmentId == null)
                    .SumAsync(q => q.QuotaBytes, cancellationToken);

                var availableInTenant = Math.Max(0, tenant.StorageQuotaBytes - (existingDeptsQuota + pendingAdminsQuota));

                if (quotaToAllocate > availableInTenant)
                {
                    return Result<UserDto>.Failure(
                        $"Hạn mức yêu cầu ({FormatBytes(quotaToAllocate)}) vượt quá dung lượng khả dụng của Tổ chức (Tenant còn trống: {FormatBytes(availableInTenant)}).");
                }
            }

            personalQuotaBytes = quotaToAllocate;
        }

        // =========================================================================
        // BƯỚC 5: TÌM VAI TRÒ 'DepartmentAdmin' CỐ ĐỊNH TRONG HỆ THỐNG
        // =========================================================================
        var departmentAdminRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "DepartmentAdmin", cancellationToken);

        if (departmentAdminRole == null)
        {
            return Result<UserDto>.Failure("Vai trò 'DepartmentAdmin' chưa được định nghĩa trong hệ thống.");
        }

        // =========================================================================
        // BƯỚC 6: XÁC ĐỊNH HÌNH THỨC TẠO TÀI KHOẢN (MỜI QUA EMAIL HAY TẠO TRỰC TIẾP)
        // - Nếu mật khẩu để trống: Coi như lời mời (isInvitation = true).
        //   Sinh mật khẩu tạm thời ngẫu nhiên và tài khoản ở trạng thái chưa kích hoạt.
        // - Nếu có truyền mật khẩu: Tài khoản được kích hoạt ngay lập tức.
        // =========================================================================
        var isInvitation = string.IsNullOrEmpty(request.Password);
        var initialPassword = isInvitation ? Guid.NewGuid().ToString("N") : request.Password!;

        // Khởi tạo Entity Người dùng (User)
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.HashPassword(initialPassword),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            TenantId = request.TenantId,
            Tenant = tenant,
            DepartmentId = department?.Id,
            Department = department,
            IsActive = !isInvitation,
            EmailConfirmed = !isInvitation
        };

        // =========================================================================
        // BƯỚC 7: GÁN VAI TRÒ 'DepartmentAdmin' CHO NGƯỜI DÙNG
        // =========================================================================
        user.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = departmentAdminRole.Id,
            User = user,
            Role = departmentAdminRole
        });

        await _context.Users.AddAsync(user, cancellationToken);

        // =========================================================================
        // BƯỚC 8: CẤP VÙNG NHỚ (QUOTA) TẠM THỜI CHO TRƯỞNG PHÒNG NẮM GIỮ
        // Bảng UserStorageQuota đóng vai trò là "ví giữ quota":
        // - Khi Trưởng phòng tự setup phòng ban, số quota này sẽ được chuyển thành Quota của phòng.
        // - Trưởng phòng sẽ tự chọn giữ lại một phần cho cá nhân, phần còn lại để chia cho nhân viên.
        // =========================================================================
        var adminStorageQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            QuotaBytes = personalQuotaBytes,
            UsedBytes = 0
        };
        await _context.UserStorageQuotas.AddAsync(adminStorageQuota, cancellationToken);

        // =========================================================================
        // BƯỚC 9: XỬ LÝ LỜI MỜI VÀ TOKEN KÍCH HOẠT (NẾU MỜI QUA GMAIL)
        // =========================================================================
        string? tokenString = null;
        if (isInvitation)
        {
            tokenString = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var invitationToken = new EmailVerificationToken(
                id: Guid.NewGuid(),
                userId: user.Id,
                token: tokenString,
                tokenType: "DepartmentAdminInvitation",
                expirationHours: 48);

            await _context.EmailVerificationTokens.AddAsync(invitationToken, cancellationToken);
        }

        // =========================================================================
        // BƯỚC 10: GHI LỊCH SỬ KIỂM TOÁN (AUDIT LOG)
        // =========================================================================
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = isInvitation ? "InviteDepartmentAdminWithQuota" : "CreateDepartmentAdminWithQuota",
            EntityName = "User",
            EntityId = user.Id,
            Details = department != null
                ? $"Tạo/Mời Trưởng phòng '{user.FullName}' cho phòng '{department.Name}'. Hạn mức cá nhân: {FormatBytes(personalQuotaBytes)}."
                : $"Tạo/Mời Trưởng phòng '{user.FullName}' (chờ tự thiết lập phòng ban). Cấp ngân sách thiết lập: {FormatBytes(personalQuotaBytes)}.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        // Lưu toàn bộ dữ liệu vào CSDL
        await _context.SaveChangesAsync(cancellationToken);

        // =========================================================================
        // BƯỚC 11: GỬI GMAIL MỜI TRƯỞNG PHÒNG KÍCH HOẠT TÀI KHOẢN (NẾU LÀ INVITATION)
        // =========================================================================
        if (isInvitation && !string.IsNullOrEmpty(tokenString))
        {
            try
            {
                await _emailService.SendDepartmentAdminInvitationAsync(
                    toEmail: user.Email,
                    recipientName: user.FullName,
                    tenantName: tenant.Name,
                    departmentName: department?.Name,
                    token: tokenString,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                // Không để lỗi SMTP làm đứt luồng tạo tài khoản
                // Token kích hoạt vẫn an toàn trong DB và user có thể nhận lại link
            }
        }

        // =========================================================================
        // BƯỚC 12: TẢI LẠI ĐẦY ĐỦ QUAN HỆ NAVIGATION PROPERTIES ĐỂ TRẢ VỀ DTO
        // =========================================================================
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
