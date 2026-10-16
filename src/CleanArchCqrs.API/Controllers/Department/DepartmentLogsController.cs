using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetAuditLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Department;

/// <summary>
/// Quản lý và tra cứu nhật ký hoạt động (Audit Logs) của nhân viên phòng ban.
/// </summary>
[Route("api/department/{departmentId:guid}/logs")]
public class DepartmentLogsController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách nhật ký hoạt động của nhân viên thuộc phòng ban.
    /// </summary>
    [Authorize(Policy = "user.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedList<AuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetDepartmentLogs(
        [FromRoute] Guid departmentId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? action = null)
    {
        var query = new GetAuditLogsQuery
        {
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
