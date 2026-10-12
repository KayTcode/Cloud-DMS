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

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
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
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
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
                return Redirect(safeReturnUrl);
            }

            return Ok(new { success = true, message = "Google login thành công.", userId = result.Login.UserId, email = result.Login.Email });
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
