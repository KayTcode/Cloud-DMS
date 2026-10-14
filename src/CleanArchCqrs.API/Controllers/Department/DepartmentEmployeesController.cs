using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.CreateEmployee;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.DeleteDepartmentEmployee;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.UpdateDepartmentEmployee;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentMemberById;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentMembers;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Department;

/// <summary>
/// Quản lý thành viên/nhân viên (Members/Employees) trong một phòng ban cụ thể (CRUD).
/// </summary>
[Route("api/department/{departmentId:guid}/employees")]
public class DepartmentEmployeesController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách thành viên phân trang thuộc phòng ban (kèm thông tin hạn mức Quota).
    /// </summary>
    [Authorize(Policy = "user.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedList<DepartmentMemberDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetDepartmentEmployees(
        [FromRoute] Guid departmentId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        [FromQuery] bool? isActive = null)
    {
        var query = new GetDepartmentMembersWithPaginationQuery
        {
            DepartmentId = departmentId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            SearchTerm = searchTerm,
            IsActive = isActive
        };

        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Lấy chi tiết thông tin một thành viên cụ thể trong phòng ban.
    /// </summary>
    [Authorize(Policy = "user.read")]
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(Result<DepartmentMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetDepartmentEmployeeById(
        [FromRoute] Guid departmentId,
        [FromRoute] Guid userId)
    {
        var query = new GetDepartmentMemberByIdQuery(departmentId, userId);
        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Thêm mới tài khoản nhân viên vào phòng ban (hoặc gửi thư mời kích hoạt nếu không nhập mật khẩu).
    /// </summary>
    [Authorize(Policy = "user.create")]
    [HttpPost]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CreateEmployee(
        [FromRoute] Guid departmentId,
        [FromBody] CreateEmployeeRequest request)
    {
        var command = new CreateEmployeeCommand
        {
            DepartmentId = departmentId,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Password = request.Password,
            StorageQuotaBytes = request.StorageQuotaBytes
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Cập nhật thông tin thành viên trong phòng ban (họ tên, số điện thoại, trạng thái hoạt động, hạn mức Quota).
    /// </summary>
    [Authorize(Policy = "user.update")]
    [HttpPut("{userId:guid}")]
    [ProducesResponseType(typeof(Result<DepartmentMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UpdateEmployee(
        [FromRoute] Guid departmentId,
        [FromRoute] Guid userId,
        [FromBody] UpdateDepartmentEmployeeRequest request)
    {
        var command = new UpdateDepartmentEmployeeCommand
        {
            DepartmentId = departmentId,
            UserId = userId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            IsActive = request.IsActive,
            StorageQuotaBytes = request.StorageQuotaBytes
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Xóa hoặc vô hiệu hóa thành viên khỏi phòng ban (thu hồi phiên đăng nhập và hoàn trả Quota thừa).
    /// </summary>
    [Authorize(Policy = "user.delete")]
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> DeleteEmployee(
        [FromRoute] Guid departmentId,
        [FromRoute] Guid userId,
        [FromQuery] bool removeFromDepartmentOnly = false)
    {
        var command = new DeleteDepartmentEmployeeCommand
        {
            DepartmentId = departmentId,
            UserId = userId,
            RemoveFromDepartmentOnly = removeFromDepartmentOnly
        };

        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}

public class CreateEmployeeRequest
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Password { get; set; }
    public long? StorageQuotaBytes { get; set; }
}

public class UpdateDepartmentEmployeeRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public long? StorageQuotaBytes { get; set; }
}
