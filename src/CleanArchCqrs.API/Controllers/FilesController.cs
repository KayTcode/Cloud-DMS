using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.Commands.DeleteFile;
using CleanArchCqrs.Application.Features.Files.Commands.PermanentDeleteFile;
using CleanArchCqrs.Application.Features.Files.Commands.RestoreFile;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Application.Features.Files.DTOs;
using CleanArchCqrs.Application.Features.Files.Queries.DownloadFile;
using CleanArchCqrs.Application.Features.Files.Queries.GetTenantFiles;
using CleanArchCqrs.Application.Features.Files.Queries.GetTrashFiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ApiControllerBase
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".php", ".vbs", ".js", ".ps1", ".msi", ".dll", ".jar", ".com", ".pif", ".scr", ".hta"
    };

    /// <summary>
    /// Uploads a single file or multiple files with Quota validation, Virus scan, Hash calculation, and Multi-Cloud routing.
    /// </summary>
    [Authorize(Policy = "file.upload")]
    [EnableRateLimiting("upload")]
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Result<MultipleFileUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Upload(
        [FromForm] List<IFormFile> files,
        [FromForm] Guid? folderId)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(Result.Failure("Không tìm thấy tệp tin đính kèm để tải lên."));
        }

        if (files.Count > 20)
        {
            return BadRequest(Result.Failure("Mỗi lần tải lên chỉ được phép gửi tối đa 20 tệp tin."));
        }

        var response = new MultipleFileUploadResultDto();

        foreach (var item in files)
        {
            if (item.Length == 0) continue;

            var ext = Path.GetExtension(item.FileName);
            if (!string.IsNullOrEmpty(ext) && BlockedExtensions.Contains(ext))
            {
                response.Failed.Add(new FileUploadFailureDto
                {
                    FileName = item.FileName,
                    FileSize = item.Length,
                    Error = $"Tệp tin '{item.FileName}' có định dạng không được phép tải lên hệ thống vì lý do bảo mật."
                });
                continue;
            }

            try
            {
                using var stream = item.OpenReadStream();
                var command = new UploadFileCommand
                {
                    FileStream = stream,
                    FileName = item.FileName,
                    ContentType = string.IsNullOrEmpty(item.ContentType) ? "application/octet-stream" : item.ContentType,
                    FileSize = item.Length,
                    FolderId = folderId
                };

                var result = await Sender.Send(command);
                if (result.IsSuccess && result.Value != null)
                {
                    response.Uploaded.Add(result.Value);
                }
                else
                {
                    response.Failed.Add(new FileUploadFailureDto
                    {
                        FileName = item.FileName,
                        FileSize = item.Length,
                        Error = result.Error ?? "Không thể lưu trữ tệp tin."
                    });
                }
            }
            catch (Exception ex)
            {
                response.Failed.Add(new FileUploadFailureDto
                {
                    FileName = item.FileName,
                    FileSize = item.Length,
                    Error = ex.Message
                });
            }
        }

        return Ok(Result<MultipleFileUploadResultDto>.Success(response));
    }

    /// <summary>
    /// Gets list of files and storage quota summary for tenant.
    /// </summary>
    [Authorize(Policy = "file.read")]
    [HttpGet]
    [ProducesResponseType(typeof(Result<TenantStorageSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetTenantFiles(
        [FromQuery] Guid? tenantId, 
        [FromQuery] Guid? folderId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null)
    {
        var query = new GetTenantFilesQuery
        {
            TenantId = tenantId,
            FolderId = folderId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            SearchTerm = searchTerm
        };

        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Downloads file from assigned Cloud Storage Provider.
    /// </summary>
    [Authorize(Policy = "file.read")]
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadFile([FromRoute] Guid id)
    {
        var query = new DownloadFileQuery(id);
        var result = await Sender.Send(query);

        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(result.Error ?? "Không thể tải tệp tin.");
        }

        return File(result.Value.Stream, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>
    /// Chuyển tệp tin vào thùng rác (Soft Delete). Chưa xóa vật lý trên Cloud và chưa giải phóng Quota.
    /// </summary>
    [Authorize(Policy = "file.delete")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult> DeleteFile([FromRoute] Guid id)
    {
        var command = new DeleteFileCommand(id);
        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Lấy danh sách tệp tin trong thùng rác (Recycle Bin) có phân quyền theo Role.
    /// </summary>
    [Authorize(Policy = "file.read")]
    [HttpGet("trash")]
    [ProducesResponseType(typeof(Result<TrashFileListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetTrashFiles(
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? departmentId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null)
    {
        var query = new GetTrashFilesQuery
        {
            TenantId = tenantId,
            DepartmentId = departmentId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            SearchTerm = searchTerm
        };
        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Khôi phục tệp tin từ thùng rác trở lại danh sách hoạt động.
    /// </summary>
    [Authorize(Policy = "file.upload")]
    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult> RestoreFile([FromRoute] Guid id)
    {
        var command = new RestoreFileCommand(id);
        var result = await Sender.Send(command);
        return HandleResult(result);
    }

    /// <summary>
    /// Xóa vĩnh viễn tệp tin khỏi Cloud Storage và giải phóng Quota.
    /// </summary>
    [Authorize(Policy = "file.delete")]
    [HttpDelete("{id:guid}/permanent")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult> PermanentDeleteFile([FromRoute] Guid id)
    {
        var command = new PermanentDeleteFileCommand(id);
        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}
