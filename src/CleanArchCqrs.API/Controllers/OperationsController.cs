using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[Route("api/operations")]
public sealed class OperationsController(
    IAuthorizationMutationCoordinator mutationCoordinator,
    ICurrentUserService currentUserService) : ApiControllerBase
{
    [HttpGet("{operationId:guid}")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperationStatusResponse>> Get(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.UserId.HasValue)
        {
            return Unauthorized();
        }

        var receipt = await mutationCoordinator.GetAsync(
            operationId,
            currentUserService.UserId.Value,
            cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        return Ok(new OperationStatusResponse(
            receipt.OperationId,
            receipt.Version,
            receipt.Status.ToString()));
    }
}

public sealed record OperationStatusResponse(Guid OperationId, long Version, string Status);
