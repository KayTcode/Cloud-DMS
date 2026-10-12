using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.GoogleLogin;

public sealed record GoogleLinkAccountCommand(string IdToken) : IRequest<GoogleLinkAccountResultDto>;

public sealed record GoogleLinkAccountResultDto(bool Success, string Message);

public sealed class GoogleLinkAccountCommandHandler : IRequestHandler<GoogleLinkAccountCommand, GoogleLinkAccountResultDto>
{
    private readonly IGoogleAuthService _googleAuthService;
    private readonly ICurrentUserService _currentUserService;

    public GoogleLinkAccountCommandHandler(IGoogleAuthService googleAuthService, ICurrentUserService currentUserService)
    {
        _googleAuthService = googleAuthService;
        _currentUserService = currentUserService;
    }

    public async Task<GoogleLinkAccountResultDto> Handle(GoogleLinkAccountCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            throw new UnauthorizedAccessException("Bạn cần đăng nhập để liên kết tài khoản Google.");
        }

        await _googleAuthService.LinkGoogleAccountAsync(
            request.IdToken,
            currentUserId.Value,
            _currentUserService.Email,
            cancellationToken);

        return new GoogleLinkAccountResultDto(true, "Tài khoản Google đã được liên kết với người dùng hiện tại.");
    }
}
