using CleanArchCqrs.Application.Features.SystemAdmin.Commands.AllocateTenantStorageQuota;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetTenantStorageQuota;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers.Admin;

public record AllocateQuotaRequest(
    Guid StorageProviderId,
    long QuotaBytes
);

/// <summary>
/// Quản lý phân bổ hạn mức lưu trữ (Quota) cho từng Tenant/Doanh nghiệp.
/// Dành cho System Admin thực hiện cấp Quota từ Cloud Storage Pool.
/// </summary>
[Route("api/admin/tenants/{tenantId:guid}/quota")]
public class TenantQuotaController : ApiControllerBase
{
    /// <summary>
    /// Lấy thông tin hạn mức Quota và dung lượng đã dùng của Tenant.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(TenantStorageQuotaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantStorageQuotaDto>> GetTenantQuota(Guid tenantId)
    {
        var result = await Sender.Send(new GetTenantStorageQuotaQuery(tenantId));
        if (result == null)
            return NotFound(new { message = $"Không tìm thấy Tenant với ID {tenantId}" });

        return Ok(result);
    }

    /// <summary>
    /// Cấp / Cập nhật Quota và gán Cloud Provider cho Tenant.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(TenantStorageQuotaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TenantStorageQuotaDto>> AllocateQuota(
        Guid tenantId, 
        [FromBody] AllocateQuotaRequest request)
    {
        try
        {
            var command = new AllocateTenantStorageQuotaCommand(
                tenantId, 
                request.StorageProviderId, 
                request.QuotaBytes);

            var result = await Sender.Send(command);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi khi cấp Quota: {ex.Message}" });
        }
    }
}
