using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Users.Commands.Logout;

public record LogoutCommand : IRequest<Result<bool>>;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result<bool>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthSessionService _sessions;

    public LogoutCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService, IAuthSessionService sessions)
    {
        _context = context;
        _currentUserService = currentUserService;
        _sessions = sessions;
    }

    public async Task<Result<bool>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            return Result<bool>.Failure("Bạn chưa xác thực danh tính người dùng.");
        }

        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == currentUserId.Value && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            await _sessions.BlockAsync(token.Id, cancellationToken);
            token.Revoke();
        }

        if (activeTokens.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}
