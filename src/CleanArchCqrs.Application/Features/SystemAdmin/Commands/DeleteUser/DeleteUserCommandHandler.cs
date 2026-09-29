using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.DeleteUser;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteUserCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

        if (user == null)
        {
            return Result.Failure($"User with ID '{request.Id}' was not found.");
        }

        // Logic: Không cho phép xóa nếu đây là SystemAdmin duy nhất trong hệ thống
        var isSystemAdmin = user.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == "SystemAdmin");
        if (isSystemAdmin)
        {
            var systemAdminCount = await _context.Users
                .Where(u => u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == "SystemAdmin"))
                .CountAsync(cancellationToken);

            if (systemAdminCount <= 1)
            {
                return Result.Failure("Cannot delete the only System Administrator in the system.");
            }
        }

        // Remove UserRoles first
        if (user.UserRoles.Any())
        {
            _context.UserRoles.RemoveRange(user.UserRoles);
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
