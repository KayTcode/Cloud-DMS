using CleanArchCqrs.Application.Users.Commands.AcceptInvitation;
using CleanArchCqrs.Application.Users.Commands.GoogleLogin;
using CleanArchCqrs.Application.Users.Commands.Login;
using CleanArchCqrs.Application.Users.Commands.RefreshToken;
using CleanArchCqrs.Application.Users.DTOs;
using CleanArchCqrs.Application.Users.Queries.GoogleLogin;
using CleanArchCqrs.Application.Users.Queries.ValidateInvitation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArchCqrs.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;

    public AuthController(IMediator mediator, IConfiguration config)
    {
        _mediator = mediator;
        _config = config;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand? command, CancellationToken cancellationToken)
    {
        var tokenValue = command?.RefreshTokenValue;
        if (string.IsNullOrWhiteSpace(tokenValue) && Request.Cookies.TryGetValue("cloud_dms_refresh_token", out var cookieToken))
        {
            tokenValue = cookieToken;
        }

        if (string.IsNullOrWhiteSpace(tokenValue))
        {
            return BadRequest(new { message = "Refresh token không được để trống." });
        }

        var result = await _mediator.Send(new RefreshTokenCommand(tokenValue), cancellationToken);
        if (!result.IsSuccess)
        {
            return Unauthorized(new { message = result.Error });
        }

        Response.Cookies.Append("cloud_dms_refresh_token", result.Value.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = result.Value.RefreshTokenExpiresAt,
            IsEssential = true,
            Path = "/"
        });

        return Ok(result.Value);
    }

    [AllowAnonymous]
    [HttpGet("google/status")]
    public async Task<IActionResult> GoogleStatus(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GoogleAuthStatusQuery(), cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("google/link")]
    public async Task<IActionResult> LinkGoogleAccount([FromBody] GoogleLinkAccountCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _mediator.Send(command, cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("google/login")]
    public async Task<IActionResult> GoogleLogin(
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.StartsWith("/"))
        {
            var referer = Request.Headers.Referer.ToString();
            if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri) && refUri.IsLoopback)
            {
                var origin = $"{refUri.Scheme}://{refUri.Authority}";
                returnUrl = string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/"
                    ? $"{origin}/auth/google/callback"
                    : $"{origin}{returnUrl}";
            }
        }

        var result = await _mediator.Send(new GoogleLoginUrlQuery(returnUrl), cancellationToken);
        return Redirect(result.AuthorizationUrl);
    }

    [AllowAnonymous]
    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? error_description,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new { message = error_description ?? error });
        }

        try
        {
            var result = await _mediator.Send(new GoogleLoginCallbackCommand(code, state), cancellationToken);

            Response.Cookies.Append("cloud_dms_refresh_token", result.Login.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = result.Login.RefreshTokenExpiresAt,
                IsEssential = true,
                Path = "/"
            });

            var safeReturnUrl = SanitizeReturnUrl(result.ReturnUrl);
            if (!string.IsNullOrWhiteSpace(safeReturnUrl))
            {
                var builder = new UriBuilder(safeReturnUrl);
                var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
                query["accessToken"] = result.Login.AccessToken;
                query["refreshToken"] = result.Login.RefreshToken;
                query["userId"] = result.Login.UserId.ToString();
                query["email"] = result.Login.Email;
                query["fullName"] = result.Login.FullName ?? "";
                query["role"] = result.Login.Role;
                if (result.Login.TenantId.HasValue)
                {
                    query["tenantId"] = result.Login.TenantId.Value.ToString();
                }
                query["permissions"] = System.Text.Json.JsonSerializer.Serialize(result.Login.Permissions);

                // Dùng Fragment (#) thay vì Query (?) theo chuẩn OAuth 2.0 SPA (RFC 6749 Section 4.2)
                // để bảo mật: không gửi token lên server logs, proxy logs hoặc rò rỉ qua header Referer
                builder.Fragment = query.ToString();
                builder.Query = string.Empty;

                return Redirect(builder.ToString());
            }

            return Ok(new
            {
                accessToken = result.Login.AccessToken,
                expiresAt = result.Login.ExpiresAt,
                userId = result.Login.UserId,
                email = result.Login.Email,
                fullName = result.Login.FullName,
                role = result.Login.Role,
                tenantId = result.Login.TenantId,
                permissions = result.Login.Permissions,
                refreshToken = result.Login.RefreshToken,
                refreshTokenExpiresAt = result.Login.RefreshTokenExpiresAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CleanArchCqrs.Application.Users.Commands.Logout.LogoutCommand(), cancellationToken);

        if (!result.IsSuccess)
        {
            return Unauthorized(new { message = result.Error });
        }

        Response.Cookies.Delete("cloud_dms_refresh_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        return Ok(new { success = true, message = "Bạn đã đăng xuất thành công." });
    }

    private string? SanitizeReturnUrl(string? returnUrl)
    {
        var frontendBaseUrl = _config["EmailSettings:FrontendBaseUrl"] ?? "http://localhost:3000";

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return $"{frontendBaseUrl.TrimEnd('/')}/auth/google/callback";
        }

        if (returnUrl.StartsWith("/", StringComparison.Ordinal))
        {
            return returnUrl == "/" 
                ? $"{frontendBaseUrl.TrimEnd('/')}/auth/google/callback" 
                : $"{frontendBaseUrl.TrimEnd('/')}{returnUrl}";
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return $"{frontendBaseUrl.TrimEnd('/')}/auth/google/callback";
        }

        if (uri.IsLoopback && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return uri.ToString();
        }

        return $"{frontendBaseUrl.TrimEnd('/')}/auth/google/callback";
    }

    [AllowAnonymous]
    [HttpGet("validate-invitation")]
    public async Task<IActionResult> ValidateInvitation(
        [FromQuery] string token, 
        [FromQuery] string email, 
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ValidateInvitationQuery(token, email), cancellationToken);
        if (!result.IsValid)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("accept-invitation")]
    public async Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptInvitationCommand command, 
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
