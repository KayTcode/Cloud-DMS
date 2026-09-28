using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.ChangeUserStatus;

public class ChangeUserStatusCommandHandler : IRequestHandler<ChangeUserStatusCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public ChangeUserStatusCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(
        ChangeUserStatusCommand request,
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

        // Logic: Không cho phép vô hiệu hóa (Inactive) nếu đây là SystemAdmin đang hoạt động duy nhất
        if (!request.IsActive && user.IsActive)
        {
            var isSystemAdmin = user.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == "SystemAdmin");

            if (isSystemAdmin)
            {
                var activeSystemAdminCount = await _context.Users
                    .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == "SystemAdmin"))
                    .CountAsync(cancellationToken);

                if (activeSystemAdminCount <= 1)
                {
                    return Result.Failure("Cannot deactivate the only active System Administrator in the system.");
                }
            }
        }

        if (request.IsActive)
        {
            user.Active();
        }
        else
        {
            user.Inactive();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
