using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public class StorageServiceFactory : IStorageServiceFactory
{
    private readonly IAesEncryptionService _encryptionService;
    private readonly string _localBasePath;

    public StorageServiceFactory(IAesEncryptionService encryptionService, IConfiguration configuration)
    {
        _encryptionService = encryptionService;
        _localBasePath = configuration["Storage:LocalBasePath"] 
                         ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "Storage");
    }

    public IStorageService CreateService(StorageProvider provider)
    {
        var rawConfigJson = _encryptionService.Decrypt(provider.ConfigEncrypted);
        return CreateServiceFromRawConfig(provider.Type, rawConfigJson);
    }

    public IStorageService CreateServiceFromRawConfig(StorageProviderType type, string rawConfigJson)
    {
        return type switch
        {
            StorageProviderType.GoogleDrive => new GoogleDriveStorageService(rawConfigJson),
            StorageProviderType.OneDrive => new OneDriveStorageService(rawConfigJson),
            StorageProviderType.Local => new LocalStorageService(_localBasePath),
            _ => throw new NotSupportedException($"Storage Provider Type {type} is not supported.")
        };
    }
}
