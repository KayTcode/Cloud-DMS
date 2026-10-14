using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using DomainRefreshToken = CleanArchCqrs.Domain.Entities.RefreshToken;

namespace CleanArchCqrs.Application.Users.Commands.AcceptInvitation;

public record AcceptInvitationCommand(
    string Token,
    string Email,
    string? NewPassword = null
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

        When(x => !string.IsNullOrEmpty(x.NewPassword), () =>
        {
            RuleFor(x => x.NewPassword!)
                .MinimumLength(6).WithMessage("Mật khẩu phải có độ dài tối thiểu 6 ký tự.")
                .MaximumLength(100).WithMessage("Mật khẩu không được vượt quá 100 ký tự.");
        });
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

        // 1. Update user password (if provided) and activate account
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            user.ChangePassword(_passwordHasher.Hash(request.NewPassword));
        }
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

        // 4. Generate JWT Token & Refresh Token so user is immediately logged in
        var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken) ?? "TenantAdmin";
        var permissions = await _userAuthorizationRepository.GetPermissionsAsync(user.Id, cancellationToken);

        var token = _jwtTokenService.GenerateAccessToken(user, role, permissions);

        var refreshToken = Guid.NewGuid().ToString("N");
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        var refreshTokenHash = HashRefreshToken(refreshToken);

        await _context.RefreshTokens.AddAsync(
            new DomainRefreshToken(
                Guid.NewGuid(),
                user.Id,
                refreshTokenHash,
                refreshTokenExpiresAt,
                null),
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new LoginResponseDto(
            token.AccessToken,
            token.ExpiresAt,
            user.Id,
            user.Email,
            user.FullName,
            role,
            user.TenantId,
            permissions.ToList(),
            refreshToken,
            refreshTokenExpiresAt,
            user.DepartmentId);
    }

    private static string HashRefreshToken(string refreshToken)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }
}
