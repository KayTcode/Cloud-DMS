using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.TenantAdmin.Commands.AllocateDepartmentStorageQuota;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using CleanArchCqrs.Application.Features.TenantAdmin.Queries.GetDepartments;
using Microsoft.AspNetCore.Authorization;
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
    [Authorize(Policy = "department.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<List<DepartmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetDepartments([FromRoute] Guid tenantId)
    {
        var query = new GetDepartmentsQuery(tenantId);
        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Điều chỉnh, cấp thêm hoặc thu hồi hạn mức lưu trữ (Quota) của phòng ban.
    /// </summary>
    /// <param name="tenantId">ID của Tenant.</param>
    /// <param name="departmentId">ID của Phòng ban.</param>
    /// <param name="request">Thông tin hạn mức điều chỉnh.</param>
    [Authorize(Policy = "storage.manage")]
    [HttpPut("{departmentId:guid}/quota")]
    [ProducesResponseType(typeof(Result<DepartmentStorageQuotaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> AllocateDepartmentQuota(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid departmentId,
        [FromBody] AllocateDepartmentQuotaRequest request)
    {
        var command = new AllocateDepartmentStorageQuotaCommand(
            tenantId,
            departmentId,
            request.QuotaBytes
        );
        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}

public record AllocateDepartmentQuotaRequest(long QuotaBytes);
