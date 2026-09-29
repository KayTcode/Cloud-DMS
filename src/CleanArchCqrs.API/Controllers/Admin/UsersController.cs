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

namespace CleanArchCqrs.API.Controllers.Admin;

/// <summary>
/// System Admin - Account & Role Management Controller.
/// Provides complete CRUD and role/permission assignment capabilities.
/// </summary>
[Route("api/admin/users")]
public class UsersController : ApiControllerBase
{
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
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        var command = new UpdateUserCommand
        {
            Id = id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            IsActive = request.IsActive
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Assigns or updates the roles and organization hierarchy (Tenant/Department) for a user.
    /// </summary>
    [HttpPut("{id:guid}/roles")]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> AssignRoles(Guid id, [FromBody] AssignUserRolesRequest request)
    {
        var command = new AssignUserRolesCommand
        {
            Id = id,
            RoleIds = request.RoleIds,
            TenantId = request.TenantId,
            DepartmentId = request.DepartmentId
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
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
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ChangeStatus(Guid id, [FromBody] ChangeUserStatusRequest request)
    {
        var result = await Sender.Send(new ChangeUserStatusCommand(id, request.IsActive));
        return HandleResult(result);
    }

    /// <summary>
    /// Deletes a user account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> DeleteUser(Guid id)
    {
        var result = await Sender.Send(new DeleteUserCommand(id));
        return HandleResult(result);
    }
}

public record UpdateUserRequest(string FirstName, string LastName, string? PhoneNumber, bool IsActive);

public record AssignUserRolesRequest(List<Guid> RoleIds, Guid? TenantId, Guid? DepartmentId);

public record ResetPasswordRequest(string NewPassword);

public record ChangeUserStatusRequest(bool IsActive);
