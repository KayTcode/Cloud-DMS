using System.Net.Mime;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.API.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Maps unhandled exceptions to standardized RFC 7807 Problem Details responses.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next, 
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi chưa được xử lý tại {Path}: {Message}", context.Request.Path, ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

        int statusCode;
        string title;
        string detail;
        IDictionary<string, string[]>? errors = null;

        switch (exception)
        {
            case ValidationException valEx:
                statusCode = StatusCodes.Status400BadRequest;
                title = "Dữ liệu không hợp lệ";
                detail = "Một hoặc nhiều trường dữ liệu không vượt qua kiểm tra hợp lệ.";
                errors = valEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );
                break;

            case KeyNotFoundException notFoundEx:
                statusCode = StatusCodes.Status404NotFound;
                title = "Không tìm thấy tài nguyên";
                detail = notFoundEx.Message;
                break;

            case UnauthorizedAccessException authEx:
                statusCode = StatusCodes.Status401Unauthorized;
                title = "Chưa xác thực hoặc không có quyền";
                detail = authEx.Message;
                break;

            case InvalidOperationException invEx:
                statusCode = StatusCodes.Status400BadRequest;
                title = "Yêu cầu nghiệp vụ không hợp lệ";
                detail = invEx.Message;
                break;

            default:
                statusCode = StatusCodes.Status500InternalServerError;
                title = "Lỗi máy chủ nội bộ";
                // Ở Production tuyệt đối không lộ ex.Message / stack trace
                detail = _env.IsDevelopment() 
                    ? exception.Message 
                    : "Đã xảy ra lỗi trong quá trình xử lý yêu cầu. Vui lòng thử lại sau.";
                break;
        }

        context.Response.StatusCode = statusCode;

        var problemDetails = new
        {
            type = $"https://httpstatuses.io/{statusCode}",
            title,
            status = statusCode,
            detail,
            instance = context.Request.Path.Value,
            errors
        };

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}
