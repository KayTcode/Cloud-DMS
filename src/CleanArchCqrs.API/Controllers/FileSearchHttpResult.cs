using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

internal static class FileSearchHttpResult
{
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action, CancellationToken ct)
    {
        try { return await action(); }
        catch (InvalidSearchRequestException) { return Error(400, "INVALID_SEARCH_REQUEST"); }
        catch (UnauthorizedAccessException) { return Error(401, "AUTHENTICATION_REQUIRED"); }
        catch (AuthorizationChangedException) { return Error(409, "AUTHORIZATION_CHANGED"); }
        catch (OperationCanceledException) { return Error(ct.IsCancellationRequested ? 499 : 504, "SEARCH_CANCELLED"); }
        // Never expose SQL, dependency payloads, candidate IDs, query text or exception messages.
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSearchHttpResult ERROR] {ex}");
            return Error(503, "SEARCH_UNAVAILABLE");
        }
    }

    private static ObjectResult Error(int status, string code) => new(new { code }) { StatusCode = status };
}
