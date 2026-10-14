using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using System.Security.Cryptography;
using System.Text;
using DomainRefreshToken = CleanArchCqrs.Domain.Entities.RefreshToken;

namespace CleanArchCqrs.Application.Users.Commands.Login
{
    public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponseDto>
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passswordHasher;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IUserAuthorizationRepository _userAuthorizationRepository;
        private readonly IApplicationDbContext _context;

        public LoginCommandHandler(
            IUserRepository userRepository,
            IPasswordHasher passswordHasher,
            IJwtTokenService jwtTokenService,
            IUserAuthorizationRepository userAuthorizationRepository,
            IApplicationDbContext context)
        {
            _userRepository = userRepository;
            _passswordHasher = passswordHasher;
            _jwtTokenService = jwtTokenService;
            _userAuthorizationRepository = userAuthorizationRepository;
            _context = context;
        }

        public async Task<LoginResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var user =
            await _userRepository.GetByEmailAsync(
                request.Email,
                cancellationToken);

            if (user is null)
            {
                throw new UnauthorizedAccessException(
                    "Invalid email or password.");
            }

            if (!user.IsActive)
            {
                throw new UnauthorizedAccessException(
                    "User account is inactive.");
            }

            var passwordValid =
                _passswordHasher.Verify(
                    request.Password,
                    user.PasswordHash);

            if (!passwordValid)
            {
                throw new UnauthorizedAccessException(
                    "Invalid email or password.");
            }

            if (!user.EmailConfirmed)
            {
                throw new UnauthorizedAccessException(
                    "Email chưa được xác nhận. Vui lòng xác nhận email trước khi đăng nhập.");
            }

            var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken);
            var permissions =
                await _userAuthorizationRepository.GetPermissionsAsync(
                    user.Id,
                    cancellationToken);

            var resolvedRole = role ?? "Employee";
            var token =
                _jwtTokenService.GenerateAccessToken(user, resolvedRole, permissions);

            user.MarkLogin();
            _context.Users.Update(user);

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
                resolvedRole,
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
}
