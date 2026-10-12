using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace CleanArchCqrs.Application.Users.Commands.RefreshTokenAuth;

public record RefreshTokenCommand(string RefreshToken) : IRequest<LoginResponseDto>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Mã làm mới (Refresh Token) không được để trống.");
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, LoginResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IUserAuthorizationRepository _userAuthorizationRepository;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IJwtTokenService jwtTokenService,
        IUserAuthorizationRepository userAuthorizationRepository)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _userAuthorizationRepository = userAuthorizationRepository;
    }

    public async Task<LoginResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var rawToken = request.RefreshToken.Trim();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        // 1. Tìm bản ghi RefreshToken khớp với mã băm
        var tokenRecord = await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (tokenRecord == null)
        {
            throw new UnauthorizedAccessException("Mã làm mới (Refresh Token) không hợp lệ.");
        }

        // 2. Chống Token Reuse / Bị đánh cắp (Replay Attack):
        // Nếu token này đã bị thu hồi (RevokedAt != null) mà lại tiếp tục được gửi lên,
        // đây là dấu hiệu token đã bị kẻ tấn công chiếm dụng ➔ Lập tức thu hồi TOÀN BỘ phiên của User!
        if (tokenRecord.RevokedAt != null)
        {
            var userTokens = await _context.RefreshTokens
                .Where(t => t.UserId == tokenRecord.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var t in userTokens)
            {
                t.Revoke("token_reuse_detected");
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Cảnh báo bảo mật: Phát hiện mã làm mới đã bị tái sử dụng. Toàn bộ phiên đăng nhập đã bị hủy!");
        }

        // 3. Kiểm tra hết hạn TTL
        if (tokenRecord.ExpiresAt <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Mã làm mới (Refresh Token) đã hết hạn. Vui lòng đăng nhập lại.");
        }

        // 4. Kiểm tra tài khoản người dùng
        var user = tokenRecord.User;
        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Tài khoản người dùng đã bị khóa hoặc không tồn tại.");
        }

        // 5. Token Rotation (Xoay vòng): Thu hồi token hiện tại và cấp token mới
        var newRawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var newTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newRawRefreshToken))).ToLowerInvariant();

        tokenRecord.Revoke(newTokenHash);

        var newRefreshTokenRecord = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            newTokenHash,
            DateTime.UtcNow.AddDays(7),
            null);

        _context.RefreshTokens.Add(newRefreshTokenRecord);

        // 6. Cấp Access Token mới (TTL 15 phút)
        var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken) ?? "Employee";
        var permissions = await _userAuthorizationRepository.GetPermissionsAsync(user.Id, cancellationToken);
        var accessTokenResult = _jwtTokenService.GenerateAccessToken(user, role, permissions);

        await _context.SaveChangesAsync(cancellationToken);

        return new LoginResponseDto(
            accessTokenResult.AccessToken,
            newRawRefreshToken,
            accessTokenResult.ExpiresAt,
            user.Id,
            user.Email,
            user.FullName,
            role,
            user.TenantId,
            permissions.ToList());
    }
}
