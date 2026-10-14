using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Users.DTOs;
using CleanArchCqrs.Application.Features.Users.Queries.GetMyStorageQuota;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

/// <summary>
/// Controller xử lý các tác vụ liên quan đến hạn mức dung lượng của người dùng cá nhân.
/// </summary>
[Authorize]
[ApiController]
[Route("api/users")]
public class UserQuotaController : ApiControllerBase
{
    /// <summary>
    /// Lấy thông tin hạn mức dung lượng lưu trữ cá nhân của người dùng hiện tại.
    /// </summary>
    [HttpGet("me/quota")]
    [ProducesResponseType(typeof(Result<PersonalStorageQuotaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result<PersonalStorageQuotaDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<PersonalStorageQuotaDto>>> GetMyQuota()
    {
        var result = await Sender.Send(new GetMyStorageQuotaQuery());
        return HandleResult(result);
    }
}
