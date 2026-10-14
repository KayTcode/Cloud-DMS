using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using DomainRefreshToken = CleanArchCqrs.Domain.Entities.RefreshToken;

namespace CleanArchCqrs.Application.Users.Commands.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshTokenValue) : IRequest<Result<LoginResponseDto>>;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<LoginResponseDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserAuthorizationRepository _userAuthorizationRepository;
    private readonly IJwtTokenService _jwtTokenService;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IUserAuthorizationRepository userAuthorizationRepository,
        IJwtTokenService jwtTokenService)
    {
        _context = context;
        _userAuthorizationRepository = userAuthorizationRepository;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<Result<LoginResponseDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshTokenValue))
        {
            return Result<LoginResponseDto>.Failure("Refresh token không hợp lệ.");
        }

        var refreshTokenHash = HashRefreshToken(request.RefreshTokenValue);

        var tokenRecord = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == refreshTokenHash, cancellationToken);

        if (tokenRecord is null)
        {
            return Result<LoginResponseDto>.Failure("Refresh token không tồn tại hoặc đã bị thu hồi.");
        }

        if (!tokenRecord.IsActive)
        {
            return Result<LoginResponseDto>.Failure("Refresh token đã hết hạn hoặc đã bị vô hiệu hóa.");
        }

        var user = tokenRecord.User;
        var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken) ?? "Employee";
        var permissions = await _userAuthorizationRepository.GetPermissionsAsync(user.Id, cancellationToken);

        var newRefreshTokenValue = Guid.NewGuid().ToString("N");
        var newRefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        var newRefreshTokenHash = HashRefreshToken(newRefreshTokenValue);

        tokenRecord.Revoke(newRefreshTokenHash);

        await _context.RefreshTokens.AddAsync(
            new DomainRefreshToken(
                Guid.NewGuid(),
                user.Id,
                newRefreshTokenHash,
                newRefreshTokenExpiresAt,
                null),
            cancellationToken);

        var generatedToken = _jwtTokenService.GenerateAccessToken(user, role, permissions);

        await _context.SaveChangesAsync(cancellationToken);

        return Result<LoginResponseDto>.Success(new LoginResponseDto(
            generatedToken.AccessToken,
            generatedToken.ExpiresAt,
            user.Id,
            user.Email,
            user.FullName,
            role,
            user.TenantId,
            permissions.ToList(),
            newRefreshTokenValue,
            newRefreshTokenExpiresAt,
            user.DepartmentId));
    }

    private static string HashRefreshToken(string refreshToken)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }
}
