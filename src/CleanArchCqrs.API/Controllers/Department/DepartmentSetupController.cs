using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.SetupDepartment;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Department;

/// <summary>
/// Quản lý thiết lập ban đầu cho phòng ban do chính Trưởng phòng (Department Admin) thực hiện.
/// </summary>
[Route("api/department")]
public class DepartmentSetupController : ApiControllerBase
{
    /// <summary>
    /// Trưởng phòng tự thiết lập thông tin phòng ban (Tên, Mã phòng, Thư mục gốc) từ vùng nhớ Quota được cấp.
    /// </summary>
    /// <param name="request">Thông tin phòng ban cần thiết lập.</param>
    [Authorize(Policy = "department.create")]
    [HttpPost("setup")]
    [ProducesResponseType(typeof(Result<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SetupDepartment([FromBody] SetupDepartmentRequest request)
    {
        var command = new SetupDepartmentCommand
        {
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            AdminPersonalQuotaGB = request.AdminPersonalQuotaGB,
            AdminPersonalQuotaBytes = request.AdminPersonalQuotaBytes
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}

public record SetupDepartmentRequest(
    string Name,
    string Code,
    string? Description = null,
    double? AdminPersonalQuotaGB = null,
    long? AdminPersonalQuotaBytes = null);
