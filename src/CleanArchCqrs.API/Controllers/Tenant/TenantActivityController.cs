using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetAuditLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Tenant;

/// <summary>
/// Quản lý và tra cứu nhật ký hoạt động (Audit Logs) của toàn bộ Tổ chức/Tenant.
/// Cho phép Tenant Admin xem log của các Phòng ban và Nhân viên thuộc Tenant.
/// </summary>
[Route("api/tenants/{tenantId:guid}/activity")]
public class TenantActivityController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách nhật ký hoạt động của các phòng ban và nhân sự trong Tenant.
    /// </summary>
    [Authorize(Policy = "user.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedList<AuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetTenantActivity(
        [FromRoute] Guid tenantId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? action = null,
        [FromQuery] Guid? departmentId = null)
    {
        var query = new GetAuditLogsQuery
        {
            TenantId = tenantId,
            DepartmentId = departmentId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            SearchTerm = searchTerm,
            Action = action
        };

        var result = await Sender.Send(query);
        return HandleResult(result);
    }
}
