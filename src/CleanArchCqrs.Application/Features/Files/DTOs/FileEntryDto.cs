namespace CleanArchCqrs.Application.Features.Files.DTOs;

public record FileEntryDto(
    Guid Id,
    Guid TenantId,
    Guid OwnerId,
    string OwnerName,
    Guid? FolderId,
    string Name,
    string StorageKey,
    string ContentType,
    long SizeBytes,
    string? Hash,
    string ScanStatus,
    Guid? StorageProviderId,
    string? StorageProviderName,
    string? StorageProviderType,
    DateTime CreatedAt,
    Guid? DepartmentId = null,
    string? DepartmentName = null
);

public record TenantStorageSummaryDto(
    Guid TenantId,
    string TenantName,
    long QuotaBytes,
    long UsedBytes,
    long AvailableBytes,
    double UsagePercentage,
    Guid? StorageProviderId,
    string? StorageProviderName,
    string? StorageProviderType,
    List<FileEntryDto> Files,
    int TotalCount = 0,
    int PageNumber = 1,
    int PageSize = 20,
    bool HasMore = false
);
