using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Infrastructure.Persistence.Configuations;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CleanArchCqrs.Infrastructure.Services
{
    public class JwtTokenService : IJwtTokenService
    {
        private readonly JwtOptions _options;

        public JwtTokenService(IOptions<JwtOptions> options)
        {
            _options = options.Value;
        }
        public JwtTokenResult GenerateAccessToken(User user, string role, IEnumerable<string> permissions)
        {
            var expiresAt =
            DateTime.UtcNow.AddMinutes(
                _options.ExpirationMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),

                new(JwtRegisteredClaimNames.Email, user.Email),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, role)
            };
            foreach (var permission in permissions.Distinct())
            {
                claims.Add(new Claim("permission", permission));
            }

            if (user.TenantId.HasValue)
            {
                claims.Add(
                    new Claim(
                        "tenantId",
                        user.TenantId.Value.ToString()));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    _options.SecretKey));

            var credentials =
                new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            var accessToken =
                new JwtSecurityTokenHandler()
                    .WriteToken(token);

            return new JwtTokenResult(
                accessToken,
                expiresAt);
        }
    }
}
