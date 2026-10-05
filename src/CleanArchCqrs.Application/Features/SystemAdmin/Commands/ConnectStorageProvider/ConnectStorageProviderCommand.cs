using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.ConnectStorageProvider;

public record ConnectStorageProviderCommand(
    string Name,
    StorageProviderType Type,
    string ConfigJson
) : IRequest<StorageProviderDto>;

public class ConnectStorageProviderCommandHandler : IRequestHandler<ConnectStorageProviderCommand, StorageProviderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageServiceFactory _storageServiceFactory;
    private readonly IAesEncryptionService _encryptionService;

    public ConnectStorageProviderCommandHandler(
        IApplicationDbContext context,
        IStorageServiceFactory storageServiceFactory,
        IAesEncryptionService encryptionService)
    {
        _context = context;
        _storageServiceFactory = storageServiceFactory;
        _encryptionService = encryptionService;
    }

    public async Task<StorageProviderDto> Handle(ConnectStorageProviderCommand request, CancellationToken cancellationToken)
    {
        // 1. Tạo adapter tạm thời để test kết nối và lấy quota thực
        var adapter = _storageServiceFactory.CreateServiceFromRawConfig(request.Type, request.ConfigJson);
        var quota = await adapter.GetProviderQuotaAsync(cancellationToken);

        // 2. Tạo thư mục gốc /CloudDMS_Root trên Cloud
        string? rootFolderId = null;
        try
        {
            rootFolderId = await adapter.CreateFolderAsync("CloudDMS_Root", null, cancellationToken);
        }
        catch
        {
            // Fallback nếu quyền chỉ đọc hoặc root có sẵn
            rootFolderId = "CloudDMS_Root";
        }

        // 3. Mã hóa credentials bằng AES-256
        var encryptedConfig = _encryptionService.Encrypt(request.ConfigJson);

        // 4. Lưu vào Database
        var provider = new StorageProvider(
            name: request.Name,
            type: request.Type,
            configEncrypted: encryptedConfig,
            totalCapacityBytes: quota.TotalCapacityBytes,
            usedCapacityBytes: quota.UsedCapacityBytes,
            rootFolderId: rootFolderId
        );

        _context.StorageProviders.Add(provider);
        await _context.SaveChangesAsync(cancellationToken);

        return new StorageProviderDto
        {
            Id = provider.Id,
            Name = provider.Name,
            Type = provider.Type,
            TotalCapacityBytes = provider.TotalCapacityBytes,
            UsedCapacityBytes = provider.UsedCapacityBytes,
            RootFolderId = provider.RootFolderId,
            IsActive = provider.IsActive,
            Status = provider.Status,
            CreatedAt = provider.CreatedAt,
            LastSyncAt = provider.LastSyncAt,
            TenantsCount = 0
        };
    }
}
