using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetAuditLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CleanArchCqrs.API.Controllers.Admin;

/// <summary>
/// System Admin - System Audit Logs Controller.
/// Provides paginated and filterable access to all system activity trails.
/// </summary>
[Route("api/admin/audit-logs")]
public class AuditLogsController : ApiControllerBase
{
    /// <summary>
    /// Gets a paginated list of audit logs across the system.
    /// </summary>
    [Authorize(Policy = "user.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedList<AuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetAuditLogs([FromQuery] GetAuditLogsQuery query)
    {
        var result = await Sender.Send(query);
        return HandleResult(result);
    }
}
