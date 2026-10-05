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
    DateTime CreatedAt
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
    List<FileEntryDto> Files
);
