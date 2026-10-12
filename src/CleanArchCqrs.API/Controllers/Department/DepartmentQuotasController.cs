using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.AllocateUserStorageQuota;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentUsersStorageQuota;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Department;

/// <summary>
/// Quản lý phân bổ, điều chỉnh và thu hồi hạn mức lưu trữ (Quota) cho thành viên trong phòng ban.
/// </summary>
[Route("api/department/{departmentId:guid}/quota")]
public class DepartmentQuotasController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách thành viên trong phòng ban kèm thông tin hạn mức (Quota) và dung lượng đã dùng.
    /// </summary>
    /// <param name="departmentId">ID của Phòng ban.</param>
    [Authorize(Policy = "user.read")]
    [HttpGet("users")]
    [ProducesResponseType(typeof(Result<DepartmentUsersQuotaSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetDepartmentUsersQuota([FromRoute] Guid departmentId)
    {
        var query = new GetDepartmentUsersStorageQuotaQuery(departmentId);
        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Điều chỉnh, cấp thêm hoặc thu hồi hạn mức lưu trữ (Quota) của một nhân viên trong phòng ban.
    /// </summary>
    /// <param name="departmentId">ID của Phòng ban.</param>
    /// <param name="userId">ID của người dùng/nhân viên.</param>
    /// <param name="request">Hạn mức điều chỉnh (tính bằng Bytes).</param>
    [Authorize(Policy = "storage.manage")]
    [HttpPut("users/{userId:guid}")]
    [ProducesResponseType(typeof(Result<UserStorageQuotaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> AllocateUserQuota(
        [FromRoute] Guid departmentId,
        [FromRoute] Guid userId,
        [FromBody] AllocateUserQuotaRequest request)
    {
        var command = new AllocateUserStorageQuotaCommand(departmentId, userId, request.QuotaBytes);
        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}

public record AllocateUserQuotaRequest(long QuotaBytes);
