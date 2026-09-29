using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using CleanArchCqrs.Application.Features.TenantAdmin.Queries.GetDepartments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Tenant;

/// <summary>
/// Tenant Admin - Departments Management Controller.
/// Provides querying and configuration of organizational departments for a tenant.
/// </summary>
[Route("api/tenant/{tenantId:guid}/departments")]
public class DepartmentsController : ApiControllerBase
{
    /// <summary>
    /// Gets all configured departments within the specified tenant including member counts and department leads.
    /// </summary>
    /// <param name="tenantId">The ID of the Tenant.</param>
    [HttpGet]
    [ProducesResponseType(typeof(Result<List<DepartmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetDepartments([FromRoute] Guid tenantId)
    {
        var query = new GetDepartmentsQuery(tenantId);
        var result = await Sender.Send(query);
        return HandleResult(result);
    }
}
