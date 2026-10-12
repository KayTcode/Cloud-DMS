using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GoogleLogin;

public sealed record GoogleLoginUrlQuery(string? ReturnUrl = null) : IRequest<GoogleLoginUrlResultDto>;

public sealed record GoogleLoginUrlResultDto(string AuthorizationUrl);

public sealed class GoogleLoginUrlQueryHandler : IRequestHandler<GoogleLoginUrlQuery, GoogleLoginUrlResultDto>
{
    private readonly IGoogleAuthService _googleAuthService;

    public GoogleLoginUrlQueryHandler(IGoogleAuthService googleAuthService)
    {
        _googleAuthService = googleAuthService;
    }

    public async Task<GoogleLoginUrlResultDto> Handle(GoogleLoginUrlQuery request, CancellationToken cancellationToken)
    {
        var authorizationUrl = await _googleAuthService.BuildAuthorizationUrlAsync(request.ReturnUrl, cancellationToken);
        return new GoogleLoginUrlResultDto(authorizationUrl);
    }
}
