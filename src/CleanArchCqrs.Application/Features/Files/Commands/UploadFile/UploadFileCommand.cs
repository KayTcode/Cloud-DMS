using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public record UploadFileCommand : IRequest<Result<FileEntryDto>>
{
    public required Stream FileStream { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public Guid? FolderId { get; init; }
    public Guid? TenantIdOverride { get; init; }
    public Guid? UserIdOverride { get; init; }
    public Guid? StorageProviderIdOverride { get; init; }
}
