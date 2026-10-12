using CleanArchCqrs.Application.Users.Commands.RefreshToken;
using CleanArchCqrs.Application.Users.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RefreshController : ControllerBase
{
    private readonly IMediator _mediator;

    public RefreshController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [Authorize]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        var refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            refreshToken = Request.Cookies["cloud_dms_refresh_token"];
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token không hợp lệ hoặc đã hết hạn." });
        }

        var result = await _mediator.Send(new RefreshTokenCommand(refreshToken), cancellationToken);

        if (!result.IsSuccess)
        {
            return Unauthorized(new { message = result.Error });
        }

        return Ok(result.Value);
    }
}

public sealed record RefreshTokenRequest(string? RefreshToken);