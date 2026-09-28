using CleanArchCqrs.Application.Users.DTOs;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Users.Commands.Login
{
    public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResponseDto>;

}
