using CleanArchCqrs.Application.Users.DTOs;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IGoogleAuthService
{
    Task<string> BuildAuthorizationUrlAsync(string? returnUrl = null, CancellationToken cancellationToken = default);
    Task<GoogleLoginResultDto> SignInWithGoogleAsync(string code, string state, CancellationToken cancellationToken = default);
    Task LinkGoogleAccountAsync(string idToken, Guid currentUserId, string? currentUserEmail, CancellationToken cancellationToken = default);
    Task<GoogleAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record GoogleLoginResultDto(
    LoginResponseDto Login,
    string? ReturnUrl = null);

public sealed record GoogleAuthStatusDto(
    bool IsConfigured,
    string? Issuer,
    string? RedirectUri,
    IReadOnlyList<string> AllowedDomains,
    bool HasClientId,
    bool HasClientSecret,
    bool HasRedirectUri);
