using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public sealed class ObjectReadUrlService(IApplicationDbContext db, IAesEncryptionService encryption) : IObjectReadUrlService
{
    public async Task<ObjectReadAccess> CreateAsync(ReadUrlRequest request, CancellationToken cancellationToken)
    {
        if (request.Purpose is not ("download" or "inline" or "stream"))
            throw new ArgumentException("Invalid read purpose.");
        if (request.Purpose == "inline" && request.ContentType is not
            ("application/pdf" or "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "text/plain")
            || request.Purpose == "stream" && request.ContentType is not
            ("audio/mpeg" or "audio/mp4" or "audio/ogg" or "audio/wav" or "video/mp4" or "video/webm"))
            throw new ArgumentException("Unsupported MIME for read purpose.");
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == request.ProviderId && p.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Storage provider is unavailable.");
        if (provider.Type != StorageProviderType.S3)
            throw new NotSupportedException("This storage provider does not support direct read URLs.");
        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromMinutes(10)
            || string.IsNullOrWhiteSpace(request.ObjectKey))
            throw new ArgumentException("Invalid read URL request.");
        using var adapter = new S3StorageService(encryption.Decrypt(provider.ConfigEncrypted));
        return await adapter.CreateReadUrlAsync(request.ObjectKey, request.FileName,
            request.ContentType, request.Purpose, request.Lifetime, cancellationToken);
    }
}
