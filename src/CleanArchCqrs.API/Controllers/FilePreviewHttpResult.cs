using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using Microsoft.AspNetCore.Mvc;
namespace CleanArchCqrs.API.Controllers;
internal static class FilePreviewHttpResult
{
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action, CancellationToken ct)
    {
        try { return await action(); }
        catch (InvalidSearchRequestException) { return Error(400, "INVALID_TEXT_CURSOR_OR_LIMIT"); }
        catch (UnauthorizedAccessException) { return Error(401, "AUTHENTICATION_REQUIRED"); }
        catch (AuthorizationChangedException) { return Error(409, "AUTHORIZATION_CHANGED"); }
        catch (OperationCanceledException) { return Error(ct.IsCancellationRequested ? 499 : 504, "READ_CANCELLED"); }
        catch (Exception) { return Error(503, "READ_UNAVAILABLE"); }
    }
    public static ActionResult Preview(PreviewResult result) => new ObjectResult(result)
    {
        StatusCode = result.Status switch { "pending" => 202, "unavailable" => 404,
            "unsupported" => 422, "failed" => 503, _ => 200 }
    };
    private static ObjectResult Error(int status, string code) => new(new { code }) { StatusCode = status };
}
