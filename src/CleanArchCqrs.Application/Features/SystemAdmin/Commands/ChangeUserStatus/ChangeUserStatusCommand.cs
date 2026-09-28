using CleanArchCqrs.Application.Common.Models;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.ChangeUserStatus;

public record ChangeUserStatusCommand(Guid Id, bool IsActive) : IRequest<Result>;
