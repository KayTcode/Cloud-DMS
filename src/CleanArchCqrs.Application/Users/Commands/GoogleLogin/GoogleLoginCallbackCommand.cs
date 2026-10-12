using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Users.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.GoogleLogin;

public sealed record GoogleLoginCallbackCommand(
    string? Code,
    string? State) : IRequest<GoogleLoginResultDto>;

public sealed class GoogleLoginCallbackCommandHandler : IRequestHandler<GoogleLoginCallbackCommand, GoogleLoginResultDto>
{
    private readonly IGoogleAuthService _googleAuthService;

    public GoogleLoginCallbackCommandHandler(IGoogleAuthService googleAuthService)
    {
        _googleAuthService = googleAuthService;
    }

    public async Task<GoogleLoginResultDto> Handle(GoogleLoginCallbackCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new InvalidOperationException("Missing Google authorization code.");
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            throw new InvalidOperationException("Missing Google state parameter.");
        }

        return await _googleAuthService.SignInWithGoogleAsync(request.Code, request.State, cancellationToken);
    }
}
