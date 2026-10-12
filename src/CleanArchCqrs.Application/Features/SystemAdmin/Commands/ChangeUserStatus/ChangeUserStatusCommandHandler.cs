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
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

        if (user == null)
        {
            return Result.Failure($"User with ID '{request.Id}' was not found.");
        }

        // Logic: Không cho phép vô hiệu hóa (Inactive) nếu đây là SystemAdmin đang hoạt động duy nhất
        if (!request.IsActive && user.IsActive)
        {
            var isSystemAdmin = await _context.UserRoles
                .AnyAsync(
                    userRole => userRole.UserId == user.Id
                        && userRole.Role.Name == "SystemAdmin",
                    cancellationToken);

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

        var statusChanged = user.IsActive != request.IsActive;
        if (request.IsActive)
        {
            user.Active();
        }
        else
        {
            user.Inactive();

            // Thu hồi toàn bộ Refresh Token của user khi bị vô hiệu hóa (P3-02 / BUG-18)
            var activeTokens = await _context.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.Revoke("user_deactivated");
            }
        }

        if (statusChanged)
        {
            user.AuthorizationVersion = checked(user.AuthorizationVersion + 1);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
