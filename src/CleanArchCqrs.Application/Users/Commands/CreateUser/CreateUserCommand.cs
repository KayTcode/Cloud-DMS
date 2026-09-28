using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Users.Commands.CreateUser
{
    public sealed record CreateUserCommand(
    Guid? TenantId,
    Guid? DepartmentId,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber
) : IRequest<Guid>;
}
