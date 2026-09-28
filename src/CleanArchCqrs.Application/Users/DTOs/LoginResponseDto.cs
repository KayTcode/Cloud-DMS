using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Users.DTOs
{
    public sealed record LoginResponseDto(
    string AccessToken,
    DateTime ExpiresAt
);
}
