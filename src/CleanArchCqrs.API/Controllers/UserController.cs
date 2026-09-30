using CleanArchCqrs.Application.Users.Commands.CreateUser;
using CleanArchCqrs.Application.Users.Commands.Login;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IMediator _mediator;
        public UserController(IMediator mediator)
        {
            _mediator = mediator;
        }
        [Authorize(Policy = "user.create")]
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command, CancellationToken cancellationToken)
        {
            var userId = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = userId }, new { id = userId });
        }
        [HttpGet("{id:guid}")]
        public IActionResult GetById(Guid id)
        {
            return Ok();
        }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _mediator.Send(
                    command,
                    cancellationToken);

                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
            }
        }
    }
}
