using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositorires;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Users.Commands.AcceptInvitation;

public record AcceptInvitationCommand(
    string Token,
    string Email,
    string NewPassword
) : IRequest<LoginResponseDto>;

public class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Mã xác minh (Token) không được để trống.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Định dạng email không hợp lệ.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
            .MinimumLength(6).WithMessage("Mật khẩu phải có độ dài tối thiểu 6 ký tự.")
            .MaximumLength(100).WithMessage("Mật khẩu không được vượt quá 100 ký tự.");
    }
}

public class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, LoginResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IUserAuthorizationRepository _userAuthorizationRepository;

    public AcceptInvitationCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IUserAuthorizationRepository userAuthorizationRepository)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _userAuthorizationRepository = userAuthorizationRepository;
    }

    public async Task<LoginResponseDto> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var tokenRecord = await _context.EmailVerificationTokens
            .Include(t => t.User)
                .ThenInclude(u => u.Tenant)
            .FirstOrDefaultAsync(t => t.Token == request.Token.Trim() 
                                   && t.User.Email.ToLower() == normalizedEmail, cancellationToken);

        if (tokenRecord == null)
        {
            throw new KeyNotFoundException("Mã kích hoạt không tồn tại hoặc email không trùng khớp.");
        }

        if (tokenRecord.IsUsed)
        {
            throw new InvalidOperationException("Mã kích hoạt này đã được sử dụng trước đó. Vui lòng tiến hành đăng nhập.");
        }

        if (tokenRecord.ExpiresAt < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Mã kích hoạt đã hết hạn (quá 48 giờ). Vui lòng liên hệ Quản trị viên để được cấp lại.");
        }

        var user = tokenRecord.User;

        // 1. Update user password and activate account
        user.ChangePassword(_passwordHasher.Hash(request.NewPassword));
        user.Active();
        user.EmailConfirmed = true;

        // 2. Mark token as used
        tokenRecord.MarkAsUsed();

        // 3. Ensure UserStorageQuota exists for the tenant admin
        var hasQuota = await _context.UserStorageQuotas
            .AnyAsync(q => q.UserId == user.Id, cancellationToken);

        if (!hasQuota && user.Tenant != null)
        {
            var defaultQuotaBytes = user.Tenant.StorageQuotaBytes > 0 
                ? user.Tenant.StorageQuotaBytes 
                : 10L * 1024 * 1024 * 1024; // 10 GB

            var userQuota = new UserStorageQuota
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                QuotaBytes = defaultQuotaBytes,
                UsedBytes = 0
            };

            await _context.UserStorageQuotas.AddAsync(userQuota, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // 4. Generate JWT Token so Tenant Admin is immediately logged in
        var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken) ?? "TenantAdmin";
        var permissions = await _userAuthorizationRepository.GetPermissionsAsync(user.Id, cancellationToken);

        var token = _jwtTokenService.GenerateAccessToken(user, role, permissions);

        return new LoginResponseDto(
            token.AccessToken,
            token.ExpiresAt,
            user.Id,
            user.Email,
            user.FullName,
            role,
            user.TenantId,
            permissions.ToList());
    }
}
