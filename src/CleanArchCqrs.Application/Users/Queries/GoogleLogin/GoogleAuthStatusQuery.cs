using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GoogleLogin;

public sealed record GoogleAuthStatusQuery : IRequest<GoogleAuthStatusDto>;

public sealed class GoogleAuthStatusQueryHandler : IRequestHandler<GoogleAuthStatusQuery, GoogleAuthStatusDto>
{
    private readonly IGoogleAuthService _googleAuthService;

    public GoogleAuthStatusQueryHandler(IGoogleAuthService googleAuthService)
    {
        _googleAuthService = googleAuthService;
    }

    public async Task<GoogleAuthStatusDto> Handle(GoogleAuthStatusQuery request, CancellationToken cancellationToken)
    {
        return await _googleAuthService.GetStatusAsync(cancellationToken);
    }
}
