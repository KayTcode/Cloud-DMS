using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.CreateEmployee;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetUsersWithPagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Department;

/// <summary>
/// Quản lý nhân viên (Employee) trong phòng ban bởi Trưởng phòng (Department Admin).
/// </summary>
[Route("api/department/{departmentId:guid}/employees")]
public class DepartmentEmployeesController : ApiControllerBase
{
    /// <summary>
    /// Tạo tài khoản nhân viên mới trong phòng ban (hoặc gửi thư mời kích hoạt).
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
    /// Lấy danh sách nhân viên thuộc phòng ban.
    /// </summary>
    [Authorize(Policy = "user.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedList<UserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetDepartmentEmployees(
        [FromRoute] Guid departmentId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? searchTerm = null)
    {
        var query = new GetUsersWithPaginationQuery
        {
            DepartmentId = departmentId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            SearchTerm = searchTerm
        };

        var result = await Sender.Send(query);
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
