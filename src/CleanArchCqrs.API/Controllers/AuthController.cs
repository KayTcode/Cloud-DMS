using CleanArchCqrs.Application.Users.Commands.AcceptInvitation;
using CleanArchCqrs.Application.Users.Commands.Login;
using CleanArchCqrs.Application.Users.Commands.RefreshTokenAuth;
using CleanArchCqrs.Application.Users.DTOs;
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
