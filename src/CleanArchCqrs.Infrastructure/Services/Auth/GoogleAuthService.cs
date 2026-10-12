using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositorires;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchCqrs.Infrastructure.Services.Auth;

public sealed class GoogleAuthService : IGoogleAuthService
{
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string GoogleAuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string GoogleOpenIdMetadataEndpoint = "https://accounts.google.com/.well-known/openid-configuration";

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GoogleAuthOptions _options;
    private readonly IUserRepository _userRepository;
    private readonly IUserAuthorizationRepository _userAuthorizationRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;

    public GoogleAuthService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<GoogleAuthOptions> options,
        IUserRepository userRepository,
        IUserAuthorizationRepository userAuthorizationRepository,
        IJwtTokenService jwtTokenService,
        IApplicationDbContext context)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _userRepository = userRepository;
        _userAuthorizationRepository = userAuthorizationRepository;
        _jwtTokenService = jwtTokenService;
        _context = context;
    }

    public Task<string> BuildAuthorizationUrlAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        EnsureSettingsAreConfigured();

        var state = CreateOpaqueValue();
        var nonce = CreateOpaqueValue();
        var codeVerifier = CreateOpaqueValue();
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier)));
        var safeReturnUrl = SanitizeReturnUrl(returnUrl);

        _cache.Set(state, new GoogleAuthSession(codeVerifier, nonce, safeReturnUrl, DateTime.UtcNow.AddMinutes(10)), TimeSpan.FromMinutes(10));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        var authorizationUrl = GoogleAuthorizationEndpoint + "?" + string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return Task.FromResult(authorizationUrl);
    }

    public async Task LinkGoogleAccountAsync(string idToken, Guid currentUserId, string? currentUserEmail, CancellationToken cancellationToken = default)
    {
        EnsureSettingsAreConfigured();

        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new InvalidOperationException("Google ID token is required.");
        }

        var tokenInfo = await ValidateGoogleIdTokenAsync(idToken, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(currentUserEmail) &&
            !string.Equals(currentUserEmail.Trim(), tokenInfo.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Google email không khớp với tài khoản Cloud DMS đang đăng nhập.");
        }

        var user = await _userRepository.GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy người dùng hiện tại để liên kết Google.");

        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("Tài khoản người dùng đã bị vô hiệu hóa.");
        }

        var allowedDomains = _options.AllowedDomains ?? Array.Empty<string>();
        if (allowedDomains.Length > 0)
        {
            var domain = tokenInfo.Email.Contains('@') ? tokenInfo.Email[(tokenInfo.Email.IndexOf('@') + 1)..].Trim().ToLowerInvariant() : string.Empty;
            if (!allowedDomains.Any(x => string.Equals(x.Trim(), domain, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Google email domain không được phép trong cấu hình hệ thống.");
            }
        }

        var existingGoogleUser = await _userRepository.GetByGoogleSubjectAsync(tokenInfo.Sub, cancellationToken);
        if (existingGoogleUser != null && existingGoogleUser.Id != user.Id)
        {
            throw new InvalidOperationException("Google identity này đã được liên kết với người dùng khác trong hệ thống.");
        }

        if (!string.IsNullOrWhiteSpace(user.GoogleSubject) && !string.Equals(user.GoogleSubject, tokenInfo.Sub, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tài khoản Cloud DMS này đã liên kết với Google identity khác.");
        }

        if (!string.IsNullOrWhiteSpace(user.GoogleIssuer) && !string.Equals(user.GoogleIssuer, tokenInfo.Iss, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google issuer không khớp với liên kết đã có.");
        }

        user.GoogleSubject = tokenInfo.Sub;
        user.GoogleIssuer = tokenInfo.Iss;
        user.EmailConfirmed = true;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<GoogleAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new GoogleAuthStatusDto(
            IsConfigured: !string.IsNullOrWhiteSpace(_options.ClientId)
                && !string.IsNullOrWhiteSpace(_options.ClientSecret)
                && !string.IsNullOrWhiteSpace(_options.RedirectUri),
            Issuer: _options.Issuer,
            RedirectUri: _options.RedirectUri,
            AllowedDomains: (_options.AllowedDomains ?? Array.Empty<string>())
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray(),
            HasClientId: !string.IsNullOrWhiteSpace(_options.ClientId),
            HasClientSecret: !string.IsNullOrWhiteSpace(_options.ClientSecret),
            HasRedirectUri: !string.IsNullOrWhiteSpace(_options.RedirectUri));

        return Task.FromResult(status);
    }

    public async Task<GoogleLoginResultDto> SignInWithGoogleAsync(string code, string state, CancellationToken cancellationToken = default)
    {
        EnsureSettingsAreConfigured();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Google authorization code is required.");
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            throw new InvalidOperationException("Google state is required.");
        }

        if (!_cache.TryGetValue(state, out GoogleAuthSession? session) || session is null)
        {
            throw new InvalidOperationException("Google login session is invalid or expired.");
        }

        _cache.Remove(state);

        var tokenRequest = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = _options.RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = session.CodeVerifier
        };

        var tokenResponse = await _httpClient.PostAsync(GoogleTokenEndpoint, new FormUrlEncodedContent(tokenRequest), cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Google token exchange failed.");
        }

        var tokenExchange = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenExchangeResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Google token exchange returned an empty payload.");

        if (string.IsNullOrWhiteSpace(tokenExchange.IdToken))
        {
            throw new InvalidOperationException("Google token exchange did not return an ID token.");
        }

        var tokenInfo = await ValidateGoogleIdTokenAsync(tokenExchange.IdToken, session.Nonce, cancellationToken);

        var normalizedEmail = tokenInfo.Email.Trim();
        var allowedDomains = _options.AllowedDomains ?? Array.Empty<string>();
        if (allowedDomains.Length > 0)
        {
            var domain = normalizedEmail.Contains('@') ? normalizedEmail[(normalizedEmail.IndexOf('@') + 1)..].Trim().ToLowerInvariant() : string.Empty;
            if (!allowedDomains.Any(x => string.Equals(x.Trim(), domain, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Google email domain không được phép trong cấu hình hệ thống.");
            }
        }

        var user = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user == null)
        {
            throw new UnauthorizedAccessException("Không tìm thấy tài khoản Cloud DMS đang hoạt động khớp với email Google này. Tài khoản cần được quản trị viên tạo trước khi đăng nhập bằng Google.");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("Tài khoản người dùng đã bị vô hiệu hóa.");
        }

        if (!user.EmailConfirmed)
        {
            throw new UnauthorizedAccessException("Tài khoản Google chưa xác nhận email trong Cloud DMS.");
        }

        if (!string.IsNullOrWhiteSpace(user.GoogleSubject) && !string.Equals(user.GoogleSubject, tokenInfo.Sub, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google identity này đã được liên kết với người dùng khác trong hệ thống.");
        }

        if (string.IsNullOrWhiteSpace(user.GoogleSubject))
        {
            user.GoogleSubject = tokenInfo.Sub;
            user.GoogleIssuer = tokenInfo.Iss;
        }

        if (user.GoogleIssuer is not null && !string.Equals(user.GoogleIssuer, tokenInfo.Iss, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google identity issuer không khớp với liên kết đã có.");
        }

        var role = await _userAuthorizationRepository.GetRoleAsync(user.Id, cancellationToken) ?? "Employee";
        var permissions = await _userAuthorizationRepository.GetPermissionsAsync(user.Id, cancellationToken);

        var token = _jwtTokenService.GenerateAccessToken(user, role, permissions);
        var refreshToken = Guid.NewGuid().ToString("N");
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        var refreshTokenHash = HashRefreshToken(refreshToken);

        await _context.RefreshTokens.AddAsync(
            new RefreshToken(Guid.NewGuid(), user.Id, refreshTokenHash, refreshTokenExpiresAt, null),
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var login = new LoginResponseDto(
            token.AccessToken,
            token.ExpiresAt,
            user.Id,
            user.Email,
            user.FullName,
            role,
            user.TenantId,
            permissions.ToList(),
            refreshToken,
            refreshTokenExpiresAt);

        var returnUrl = session.ReturnUrl;
        return new GoogleLoginResultDto(login, returnUrl);
    }

    private async Task<GoogleTokenInfoResponse> ValidateGoogleIdTokenAsync(string idToken, string? expectedNonce = null, CancellationToken cancellationToken = default)
    {
        var metadata = await OpenIdConnectConfigurationRetriever.GetAsync(
            GoogleOpenIdMetadataEndpoint,
            new HttpDocumentRetriever(_httpClient),
            cancellationToken);

        var handler = new JwtSecurityTokenHandler();
        handler.MapInboundClaims = false;
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = metadata.SigningKeys,
            RequireSignedTokens = true
        };

        var principal = handler.ValidateToken(idToken, validationParameters, out var validatedToken);
        var jwt = validatedToken as JwtSecurityToken ?? throw new InvalidOperationException("Google token không phải JWT hợp lệ.");

        if (!string.IsNullOrWhiteSpace(expectedNonce))
        {
            var nonceClaim = principal.FindFirst("nonce")?.Value;
            if (string.IsNullOrWhiteSpace(nonceClaim) || !string.Equals(nonceClaim, expectedNonce, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Google nonce không khớp với phiên đăng nhập.");
            }
        }

        var issuer = jwt.Issuer;
        if (!string.Equals(issuer, _options.Issuer, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issuer, "https://accounts.google.com", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issuer, "accounts.google.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google issuer is invalid.");
        }

        var email = principal.FindFirst("email")?.Value ?? string.Empty;
        var emailVerified = principal.FindFirst("email_verified")?.Value;
        var sub = principal.FindFirst("sub")?.Value ?? string.Empty;
        var aud = principal.Claims.FirstOrDefault(c => c.Type == "aud")?.Value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(sub))
        {
            throw new InvalidOperationException("Google token thiếu thông tin email hoặc subject.");
        }

        if (!string.Equals(aud, _options.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google audience does not match the configured DMS client.");
        }

        if (!string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Google email is not verified.");
        }

        return new GoogleTokenInfoResponse
        {
            Aud = aud,
            Iss = issuer,
            Sub = sub,
            Email = email,
            EmailVerified = true
        };
    }

    private void EnsureSettingsAreConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("GoogleAuth:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("GoogleAuth:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("GoogleAuth:RedirectUri must be configured.");
        }
    }

    private static string? SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        if (returnUrl.StartsWith("/", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.IsLoopback && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return uri.PathAndQuery;
        }

        return null;
    }

    private static string CreateOpaqueValue()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashRefreshToken(string refreshToken)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }

    private sealed record GoogleAuthSession(string CodeVerifier, string Nonce, string? ReturnUrl, DateTime ExpiresAt);

    private sealed class GoogleTokenExchangeResponse
    {
        [JsonPropertyName("id_token")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }

    private sealed class GoogleTokenInfoResponse
    {
        public string Aud { get; set; } = string.Empty;
        public string Iss { get; set; } = string.Empty;
        public string Sub { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
    }
}
