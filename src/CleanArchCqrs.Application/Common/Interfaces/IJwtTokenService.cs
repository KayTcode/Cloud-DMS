using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Common.Interfaces
{
    public interface IJwtTokenService
    {
        JwtTokenResult GenerateAccessToken(User user);
    }
    public sealed record JwtTokenResult(
    string AccessToken,
    DateTime ExpiresAt
);
}
