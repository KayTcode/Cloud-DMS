using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
namespace CleanArchCqrs.API.Controllers;
[Route("api/file-access")]
public sealed class FileAccessController : ApiControllerBase
{
    [HttpPost("mutations")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> Mutate([FromBody] ManageFileAccessRequest request, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct) => FileMutationHttpResult.ExecuteAsync(() => Sender.Send(new ManageAccessCommand(request.Kind, request.ResourceId, request.PrincipalId, request.PrincipalKind, request.Role, request.ExpiresAt, request.ParentFolderId, idempotencyKey), ct));
}
public sealed record ManageFileAccessRequest(AccessMutationKind Kind, Guid ResourceId, Guid PrincipalId, PrincipalKind PrincipalKind, ResourceRole Role, DateTimeOffset? ExpiresAt, Guid? ParentFolderId);
