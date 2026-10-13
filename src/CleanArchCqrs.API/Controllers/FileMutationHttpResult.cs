using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
namespace CleanArchCqrs.API.Controllers;
internal static class FileMutationHttpResult
{
    internal static async Task<ActionResult> ExecuteAsync(Func<Task<MutationReceipt>> execute)
    {
        try
        {
            var receipt = await execute(); var response = new OperationStatusResponse(receipt.OperationId, receipt.Version, receipt.Status.ToString());
            return receipt.Status == AuthorizationOperationStatus.Applied ? new OkObjectResult(response) : new AcceptedResult($"/api/operations/{receipt.OperationId:D}", response);
        }
        catch (ArgumentException) { return new BadRequestObjectResult(new { error = "Yêu cầu hoặc Idempotency-Key không hợp lệ." }); }
        catch (UnauthorizedAccessException) { return new UnauthorizedResult(); }
        catch (AuthorizationChangedException) { return new ObjectResult(new { error = "Không có quyền hoặc trạng thái đã thay đổi." }) { StatusCode = 403 }; }
        catch (AuthorizationMutationConflictException) { return new ConflictObjectResult(new { error = "Idempotency-Key đã được dùng cho yêu cầu khác." }); }
        catch (AuthorizationMutationPendingException) { return new ConflictObjectResult(new { error = "Đang có thay đổi quyền; vui lòng thử lại." }); }
        catch (SecurityUnavailableException) { return new ObjectResult(new { error = "Trạng thái bảo mật chưa sẵn sàng." }) { StatusCode = 503 }; }
    }
}
