using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.Commands.DeleteFile;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Application.Features.Files.DTOs;
using CleanArchCqrs.Application.Features.Files.Queries.DownloadFile;
using CleanArchCqrs.Application.Features.Files.Queries.GetTenantFiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ApiControllerBase
{
    /// <summary>
    /// Uploads a single file or multiple files with Quota validation, Virus scan, Hash calculation, and Multi-Cloud routing.
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Result<List<FileEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Upload(
        [FromForm] List<IFormFile> files,
        [FromForm] Guid? folderId,
        [FromForm] Guid? tenantId,
        [FromForm] Guid? storageProviderId,
        [FromForm] Guid? userId)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(Result.Failure("Không tìm thấy tệp tin đính kèm để tải lên."));
        }

        var uploadedFiles = new List<FileEntryDto>();

        foreach (var item in files)
        {
            if (item.Length == 0) continue;

            using var stream = item.OpenReadStream();
            var command = new UploadFileCommand
            {
                FileStream = stream,
                FileName = item.FileName,
                ContentType = string.IsNullOrEmpty(item.ContentType) ? "application/octet-stream" : item.ContentType,
                FileSize = item.Length,
                FolderId = folderId,
                TenantIdOverride = tenantId,
                StorageProviderIdOverride = storageProviderId,
                UserIdOverride = userId
            };

            var result = await Sender.Send(command);
            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            if (result.Value != null)
            {
                uploadedFiles.Add(result.Value);
            }
        }

        return Ok(Result<List<FileEntryDto>>.Success(uploadedFiles));
    }

    /// <summary>
    /// Gets list of files and storage quota summary for tenant.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Result<TenantStorageSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetTenantFiles([FromQuery] Guid? tenantId, [FromQuery] Guid? folderId)
    {
        var query = new GetTenantFilesQuery
        {
            TenantId = tenantId,
            FolderId = folderId
        };

        var result = await Sender.Send(query);
        return HandleResult(result);
    }

    /// <summary>
    /// Downloads file from assigned Cloud Storage Provider.
    /// </summary>
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
    /// Deletes file from Cloud Storage and releases quota back to Tenant and User.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult> DeleteFile([FromRoute] Guid id)
    {
        var command = new DeleteFileCommand(id);
        var result = await Sender.Send(command);
        return HandleResult(result);
    }
}
