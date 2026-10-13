namespace CleanArchCqrs.Application.Features.Files.DTOs;

public record TrashFileItemDto(
    Guid Id,
    Guid TenantId,
    Guid OwnerId,
    string OwnerName,
    Guid? FolderId,
    string Name,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt,
    DateTime? DeletedAt,
    Guid? DeletedByUserId,
    string? DeletedByName,
    Guid? DepartmentId = null,
    string? DepartmentName = null,
    string? StorageProviderName = null
);

public record TrashFileListDto(
    List<TrashFileItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    long TotalTrashSizeBytes,
    bool HasMore = false
);
