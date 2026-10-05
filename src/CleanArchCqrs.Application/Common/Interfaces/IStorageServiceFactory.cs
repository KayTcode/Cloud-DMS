using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IStorageServiceFactory
{
    IStorageService CreateService(StorageProvider provider);
    IStorageService CreateServiceFromRawConfig(StorageProviderType type, string rawConfigJson);
}
