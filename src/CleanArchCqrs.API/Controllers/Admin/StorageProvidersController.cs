using CleanArchCqrs.Application.Features.SystemAdmin.Commands.ConnectStorageProvider;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.DeleteStorageProvider;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.TestStorageProviderConnection;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetStorageProviderAvailableQuota;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetStorageProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Admin;

/// <summary>
/// Quản lý các nhà cung cấp lưu trữ đám mây (Google Drive, OneDrive, Local).
/// Dành cho System Admin kết nối và giám sát dung lượng.
/// </summary>
[Route("api/admin/storage-providers")]
public class StorageProvidersController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách tất cả các Cloud Storage Providers đã kết nối.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<StorageProviderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StorageProviderDto>>> GetStorageProviders()
    {
        var result = await Sender.Send(new GetStorageProvidersQuery());
        return Ok(result);
    }

    /// <summary>
    /// Kiểm tra kết nối thử nghiệm đến Google Drive hoặc OneDrive trước khi lưu.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(TestConnectionResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<TestConnectionResult>> TestConnection([FromBody] TestStorageProviderConnectionCommand command)
    {
        var result = await Sender.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Đổi authorization code từ popup Microsoft login lấy refresh token và test quota tự động.
    /// </summary>
    [HttpPost("onedrive/exchange-code")]
    [ProducesResponseType(typeof(CleanArchCqrs.Application.Features.SystemAdmin.Commands.ExchangeOneDriveCode.ExchangeOneDriveCodeResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<CleanArchCqrs.Application.Features.SystemAdmin.Commands.ExchangeOneDriveCode.ExchangeOneDriveCodeResult>> ExchangeOneDriveCode([FromBody] CleanArchCqrs.Application.Features.SystemAdmin.Commands.ExchangeOneDriveCode.ExchangeOneDriveCodeCommand command)
    {
        var result = await Sender.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Kết nối mới một Cloud Storage Provider (Google Drive / OneDrive) và lưu thông tin mã hóa vào hệ thống.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StorageProviderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageProviderDto>> ConnectStorageProvider([FromBody] ConnectStorageProviderCommand command)
    {
        try
        {
            var result = await Sender.Send(command);
            return CreatedAtAction(nameof(GetStorageProviders), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy dung lượng còn lại có thể cấp (Available to Allocate) của một Cloud Provider.
    /// </summary>
    [HttpGet("{id:guid}/available-quota")]
    [ProducesResponseType(typeof(StorageProviderAvailableQuotaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StorageProviderAvailableQuotaDto>> GetAvailableQuota(Guid id)
    {
        var result = await Sender.Send(new GetStorageProviderAvailableQuotaQuery(id));
        if (result == null)
            return NotFound(new { message = $"Không tìm thấy Cloud Provider với ID {id}" });

        return Ok(result);
    }

    /// <summary>
    /// Ngắt kết nối / xóa một Storage Provider khỏi hệ thống.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteStorageProvider(Guid id)
    {
        var success = await Sender.Send(new DeleteStorageProviderCommand(id));
        if (!success)
            return NotFound(new { message = $"Không tìm thấy Cloud Provider với ID {id}" });

        return NoContent();
    }
}
