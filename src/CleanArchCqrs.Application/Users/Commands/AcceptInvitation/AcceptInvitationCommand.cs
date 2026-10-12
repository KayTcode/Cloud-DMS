using System.Data;
using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Users.Commands.AcceptInvitation;


 //Là một record đóng vai trò là DTO chứa dữ liệu đầu vào gửi từ client/API
public record AcceptInvitationCommand(
    string Token,
    string Email,
    string NewPassword
) : IRequest<LoginResponseDto>;

public class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    // Sử dụng FluentValidation để kiểm tra tính hợp lệ của dữ liệu trước khi xử lý
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

    // Xử lý logic
    public async Task<LoginResponseDto> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            try
            {
                var tokenRecord = await _context.EmailVerificationTokens
                    .Include(t => t.User)
                        .ThenInclude(u => u.Tenant)
                    .FirstOrDefaultAsync(t => t.Token == request.Token.Trim() 
                                           && t.User.Email.ToLower() == normalizedEmail, cancellationToken);

                if (tokenRecord == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new KeyNotFoundException("Mã kích hoạt không tồn tại hoặc email không trùng khớp.");
                }

                if (tokenRecord.IsUsed)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("Mã kích hoạt này đã được sử dụng trước đó. Vui lòng tiến hành đăng nhập.");
                }

                if (tokenRecord.ExpiresAt < DateTime.UtcNow)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("Mã kích hoạt đã hết hạn (quá 48 giờ). Vui lòng liên hệ Quản trị viên để được cấp lại.");
                }

                var user = tokenRecord.User;

                // 1. Update user password and activate account
                user.ChangePassword(_passwordHasher.Hash(request.NewPassword));
                user.Active();
                user.EmailConfirmed = true;

                // 2. Mark token as used
                tokenRecord.MarkAsUsed();

                // 3. Khởi tạo UserStorageQuota nếu tài khoản chưa có ví (BUG-34)
                var hasQuota = await _context.UserStorageQuotas
                    .AnyAsync(q => q.UserId == user.Id, cancellationToken);

                if (!hasQuota)
                {
                    var newQuota = new UserStorageQuota
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        QuotaBytes = 0L,
                        UsedBytes = 0L
                    };
                    await _context.UserStorageQuotas.AddAsync(newQuota, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // 4. Generate JWT Token and RefreshToken so user is immediately logged in
                var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken) ?? "TenantAdmin";
                var permissions = await _userAuthorizationRepository.GetPermissionsAsync(user.Id, cancellationToken);

                var token = _jwtTokenService.GenerateAccessToken(user, role, permissions);

                var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken))).ToLowerInvariant();

                var refreshTokenEntity = new CleanArchCqrs.Domain.Entities.RefreshToken(
                    Guid.NewGuid(),
                    user.Id,
                    tokenHash,
                    DateTime.UtcNow.AddDays(7),
                    null);

                _context.RefreshTokens.Add(refreshTokenEntity);
                await _context.SaveChangesAsync(cancellationToken);

                return new LoginResponseDto(
                    token.AccessToken,
                    rawRefreshToken,
                    token.ExpiresAt,
                    user.Id,
                    user.Email,
                    user.FullName,
                    role,
                    user.TenantId,
                    permissions.ToList());
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
