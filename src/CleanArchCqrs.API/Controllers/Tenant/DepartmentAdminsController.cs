using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.TenantAdmin.Commands.CreateDepartmentAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Tenant;

/// <summary>
/// Tenant Admin - Department Administrator Management Controller.
/// Allows Tenant Administrators to create Department Administrators for their organization.
/// </summary>
[Route("api/tenant/{tenantId:guid}/department-admins")]
public class DepartmentAdminsController : ApiControllerBase
{
    /// <summary>
    /// Creates a new Department Administrator account within the specified tenant (Department is optional).
    /// </summary>
    /// <param name="tenantId">The ID of the Tenant.</param>
    /// <param name="request">The creation payload.</param>
    [HttpPost]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CreateDepartmentAdmin(
        [FromRoute] Guid tenantId,
        [FromBody] CreateDepartmentAdminRequest request)
    {
        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenantId,
            DepartmentId = request.DepartmentId,
            Email = request.Email,
            Password = request.Password,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}

public record CreateDepartmentAdminRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    Guid? DepartmentId = null);
