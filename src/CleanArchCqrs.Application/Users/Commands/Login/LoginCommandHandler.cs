using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositorires;
using CleanArchCqrs.Application.Users.DTOs;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Users.Commands.Login
{
    public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponseDto>
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passswordHasher;
        private readonly IJwtTokenService _jwtTokenService;

        public LoginCommandHandler(IUserRepository userRepository, IPasswordHasher passswordHasher, IJwtTokenService jwtTokenService)
        {
            _userRepository = userRepository;
            _passswordHasher = passswordHasher;
            _jwtTokenService = jwtTokenService;
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

            var token =
                _jwtTokenService.GenerateAccessToken(user);

            return new LoginResponseDto(
                token.AccessToken,
                token.ExpiresAt);
        }
    }
}
