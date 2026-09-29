using CleanArchCqrs.Application.Common.Models;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.DeleteUser;

public record DeleteUserCommand(Guid Id) : IRequest<Result>;
