using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.AssignUserRoles;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.ChangeUserStatus;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.CreateUser;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.DeleteUser;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.ResetUserPassword;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.UpdateUser;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetUserById;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetUsersWithPagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CleanArchCqrs.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Controllers.Admin;

/// <summary>
/// System Admin - Account & Role Management Controller.
/// Provides complete CRUD and role/permission assignment capabilities.
/// </summary>
[Route("api/admin/users")]
public class UsersController : ApiControllerBase
{
    private readonly IAuthorizationMutationCoordinator _mutationCoordinator;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        IAuthorizationMutationCoordinator mutationCoordinator,
        ICurrentUserService currentUserService)
    {
        _mutationCoordinator = mutationCoordinator;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Gets a paginated and filterable list of all system users.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedList<UserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetUsers([FromQuery] GetUsersWithPaginationQuery query)
    {
        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Gets detailed information of a user by ID including their assigned roles and permissions.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetUserById(Guid id)
    {
        var result = await Sender.Send(new GetUserByIdQuery(id));
        return HandleResult(result);
    }

    /// <summary>
    /// Creates a new account with specified role IDs and organizational scope (Tenant/Department).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CreateUser([FromBody] CreateUserCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Updates general profile information of a user.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "user.manage")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new UpdateUserCommand
        {
            Id = id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            IsActive = request.IsActive
        };

        return ExecuteAuthorizationMutationAsync(
            idempotencyKey,
            JsonSerializer.Serialize(new
            {
                UserId = id,
                request.FirstName,
                request.LastName,
                request.PhoneNumber,
                request.IsActive
            }),
            async token =>
            {
                var result = await Sender.Send(command, token);
                if (result.IsFailure)
                {
                    throw new MutationCommandRejectedException(result);
                }

                return result;
            },
            cancellationToken);
    }

    /// <summary>
    /// Assigns or updates the roles and organization hierarchy (Tenant/Department) for a user.
    /// </summary>
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = "user.manage")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> AssignRoles(
        Guid id,
        [FromBody] AssignUserRolesRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (request.RoleIds is null)
        {
            return Task.FromResult<ActionResult>(
                BadRequest(Result.Failure("RoleIds cannot be null.")));
        }

        var command = new AssignUserRolesCommand
        {
            Id = id,
            RoleIds = request.RoleIds,
            TenantId = request.TenantId,
            DepartmentId = request.DepartmentId
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            UserId = id,
            request.TenantId,
            request.DepartmentId,
            RoleIds = request.RoleIds.Distinct().OrderBy(roleId => roleId).ToArray()
        });
        return ExecuteAuthorizationMutationAsync(
            idempotencyKey,
            canonicalPayload,
            async token =>
            {
                var result = await Sender.Send(command, token);
                if (result.IsFailure)
                {
                    throw new MutationCommandRejectedException(result);
                }

                return result;
            },
            cancellationToken);
    }

    /// <summary>
    /// Resets the password for a user account.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        var command = new ResetUserPasswordCommand
        {
            Id = id,
            NewPassword = request.NewPassword
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Activates or deactivates a user account.
    /// </summary>
    [Authorize(Policy = "user.manage")]
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ChangeStatus(
        Guid id,
        [FromBody] ChangeUserStatusRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await ExecuteAuthorizationMutationAsync(
            idempotencyKey,
            JsonSerializer.Serialize(new { UserId = id, request.IsActive }),
            async token =>
            {
                var result = await Sender.Send(
                    new ChangeUserStatusCommand(id, request.IsActive),
                    token);
                if (result.IsFailure)
                {
                    throw new MutationCommandRejectedException(result);
                }

                return result;
            },
            cancellationToken);
    }

    /// <summary>
    /// Deletes a user account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "user.manage")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> DeleteUser(
        Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        return ExecuteAuthorizationMutationAsync(
            idempotencyKey,
            JsonSerializer.Serialize(new { UserId = id, Operation = "delete" }),
            async token =>
            {
                var result = await Sender.Send(new DeleteUserCommand(id), token);
                if (result.IsFailure)
                {
                    throw new MutationCommandRejectedException(result);
                }

                return result;
            },
            cancellationToken);
    }

    private async Task<ActionResult> ExecuteAuthorizationMutationAsync(
        string? idempotencyKey,
        string canonicalPayload,
        Func<CancellationToken, Task<Result>> persistChange,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            return BadRequest(Result.Failure("A valid Idempotency-Key header is required."));
        }

        if (!_currentUserService.UserId.HasValue)
        {
            return Unauthorized();
        }

        var actorId = _currentUserService.UserId.Value;
        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        var scopedIdempotencyKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{actorId:N}:{idempotencyKey}")));
        MutationReceipt receipt;
        try
        {
            receipt = await _mutationCoordinator.ExecuteAsync(
                new MutationRequest("global", scopedIdempotencyKey, payloadHash),
                actorId,
                async token => await persistChange(token),
                cancellationToken);
        }
        catch (MutationCommandRejectedException exception)
        {
            return BadRequest(exception.Result);
        }

        var response = new OperationStatusResponse(
            receipt.OperationId,
            receipt.Version,
            receipt.Status.ToString());
        var location = $"/api/operations/{receipt.OperationId:D}";
        return receipt.Status == AuthorizationOperationStatus.Applied
            ? Ok(response)
            : Accepted(location, response);
    }
}

internal sealed class MutationCommandRejectedException(Result result) : Exception
{
    public Result Result { get; } = result;
}

public record UpdateUserRequest(string FirstName, string LastName, string? PhoneNumber, bool IsActive);

public record AssignUserRolesRequest(List<Guid> RoleIds, Guid? TenantId, Guid? DepartmentId);

public record ResetPasswordRequest(string NewPassword);

public record ChangeUserStatusRequest(bool IsActive);
