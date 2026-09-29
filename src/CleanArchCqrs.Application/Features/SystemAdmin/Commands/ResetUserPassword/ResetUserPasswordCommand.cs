using CleanArchCqrs.Application.Common.Models;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.ResetUserPassword;

public record ResetUserPasswordCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public string NewPassword { get; init; } = string.Empty;
}
