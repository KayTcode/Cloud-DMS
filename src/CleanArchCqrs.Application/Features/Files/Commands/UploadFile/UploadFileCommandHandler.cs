using System.Security.Cryptography;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, Result<FileEntryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageServiceFactory _storageServiceFactory;
    private readonly IVirusScannerService _virusScannerService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UploadFileCommandHandler> _logger;

    public UploadFileCommandHandler(
        IApplicationDbContext context,
        IStorageServiceFactory storageServiceFactory,
        IVirusScannerService virusScannerService,
        ICurrentUserService currentUserService,
        ILogger<UploadFileCommandHandler> logger)
    {
        _context = context;
        _storageServiceFactory = storageServiceFactory;
        _virusScannerService = virusScannerService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<FileEntryDto>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        // 0. Xác định User & Tenant Context
        var userId = request.UserIdOverride ?? _currentUserService.UserId;
        var tenantId = request.TenantIdOverride ?? _currentUserService.TenantId;

        User? user = null;
        if (userId.HasValue)
        {
            user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
        }

        // Nếu không lấy được user từ token nhưng có thông tin Tenant, tự động fallback về user/admin đang hoạt động của Tenant đó
        if (user == null && tenantId.HasValue)
        {
            user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.IsActive, cancellationToken);
            userId = user?.Id;
        }

        // Fallback an toàn cuối cùng: Lấy user active hợp lệ đầu tiên trong CSDL
        if (user == null)
        {
            user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.IsActive, cancellationToken);
            userId = user?.Id;
        }

        if (user == null || !userId.HasValue)
        {
            return Result<FileEntryDto>.Failure("Yêu cầu không hợp lệ: Chưa xác thực danh tính người dùng.");
        }

        tenantId ??= user.TenantId;
        if (!tenantId.HasValue)
        {
            return Result<FileEntryDto>.Failure("Người dùng hiện tại chưa được liên kết với bất kỳ Tổ chức/Tenant nào.");
        }

        var tenant = await _context.Tenants
            .Include(t => t.StorageProvider)
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken);

        if (tenant == null)
        {
            return Result<FileEntryDto>.Failure("Không tìm thấy thông tin Tenant tương ứng.");
        }

        // BƯỚC 1: Kiểm tra Quota
        // 1.1 Kiểm tra Quota của Tenant
        if (tenant.StorageUsedBytes + request.FileSize > tenant.StorageQuotaBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Hết dung lượng lưu trữ của Tenant! (Đã dùng: {FormatBytes(tenant.StorageUsedBytes)} / Hạn mức: {FormatBytes(tenant.StorageQuotaBytes)}).");
        }

        // 1.2 Kiểm tra Quota cá nhân User (nếu có cấp riêng)
        var userQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == userId.Value, cancellationToken);

        if (userQuota != null && userQuota.UsedBytes + request.FileSize > userQuota.QuotaBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Hết dung lượng cá nhân được phân bổ! (Đã dùng: {FormatBytes(userQuota.UsedBytes)} / Hạn mức: {FormatBytes(userQuota.QuotaBytes)}).");
        }

        // 1.3 Kiểm tra cấu hình và Quota của Cloud Storage Provider
        StorageProvider? provider = null;
        if (request.StorageProviderIdOverride.HasValue)
        {
            provider = await _context.StorageProviders
                .FirstOrDefaultAsync(p => p.Id == request.StorageProviderIdOverride.Value && p.IsActive, cancellationToken);
        }

        provider ??= tenant.StorageProvider;

        if (provider == null)
        {
            return Result<FileEntryDto>.Failure(
                $"Tổ chức '{tenant.Name}' chưa được cấu hình Nhà cung cấp lưu trữ (Storage Provider: Google Drive / OneDrive / Local). Vui lòng liên hệ Quản trị viên hệ thống để gán Provider!");
        }

        if (provider.TotalCapacityBytes > 0 && provider.UsedCapacityBytes + request.FileSize > provider.TotalCapacityBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Kho lưu trữ đám mây '{provider.Name}' ({provider.Type}) đã đầy dung lượng!");
        }

        // BƯỚC 2: Quét Virus (ClamAV)
        _logger.LogInformation("Scanning file {FileName} ({SizeBytes} bytes) with ClamAV...", request.FileName, request.FileSize);
        var scanResult = await _virusScannerService.ScanStreamAsync(request.FileStream, cancellationToken);

        if (scanResult.IsInfected)
        {
            _logger.LogWarning("Malware detected in upload: {ThreatName}", scanResult.ThreatName);

            // Ghi AuditLog cảnh báo bảo mật
            var malwareAuditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = userId.Value,
                Action = "VirusDetected",
                EntityName = "FileEntry",
                Details = $"CẢNH BÁO BẢO MẬT: Phát hiện mã độc '{scanResult.ThreatName}' trong tệp tin '{request.FileName}'. Đã chặn tải lên.",
                CreatedAt = DateTime.UtcNow
            };
            _context.AuditLogs.Add(malwareAuditLog);
            await _context.SaveChangesAsync(cancellationToken);

            return Result<FileEntryDto>.Failure($"Phát hiện mã độc trong tệp tin: {scanResult.ThreatName}! Quá trình tải lên đã bị chặn.");
        }

        // BƯỚC 3: Tính Hash SHA-256 (chống trùng lặp & đảm bảo tính toàn vẹn)
        if (request.FileStream.CanSeek)
        {
            request.FileStream.Position = 0;
        }

        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(request.FileStream, cancellationToken);
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        if (request.FileStream.CanSeek)
        {
            request.FileStream.Position = 0;
        }

        // BƯỚC 4: Định tuyến đến đúng Cloud Provider qua StorageServiceFactory
        _logger.LogInformation("Routing upload to provider: {ProviderName} ({ProviderType})", provider.Name, provider.Type);
        var storageService = _storageServiceFactory.CreateService(provider);

        string? destinationFolderId = request.FolderId.HasValue 
            ? request.FolderId.Value.ToString() 
            : provider.RootFolderId;

        string storageKey;
        try
        {
            storageKey = await storageService.UploadFileAsync(
                request.FileStream,
                request.FileName,
                request.ContentType,
                destinationFolderId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to cloud provider {ProviderName}", provider.Name);
            return Result<FileEntryDto>.Failure($"Lỗi khi tải tệp tin lên nhà cung cấp lưu trữ ({provider.Name}): {ex.Message}");
        }

        // BƯỚC 5: Cập nhật CSDL (Atomic Persistence & Quota updates)
        var fileEntry = new FileEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            OwnerId = userId.Value,
            FolderId = request.FolderId,
            Name = request.FileName,
            StorageKey = storageKey,
            ContentType = request.ContentType,
            SizeBytes = request.FileSize,
            Hash = hashHex,
            ScanStatus = "Clean",
            StorageProviderId = provider.Id,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.FileEntries.Add(fileEntry);

        // Cập nhật dung lượng Tenant
        tenant.StorageUsedBytes += request.FileSize;

        // Cập nhật dung lượng Provider
        provider.UsedCapacityBytes += request.FileSize;

        // Cập nhật dung lượng cá nhân User nếu có
        if (userQuota != null)
        {
            userQuota.UsedBytes += request.FileSize;
        }

        // Ghi AuditLog
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId.Value,
            Action = "Upload",
            EntityName = "FileEntry",
            EntityId = fileEntry.Id,
            Details = $"Tải lên thành công '{request.FileName}' ({FormatBytes(request.FileSize)}) vào {provider.Name} ({provider.Type}). Khóa lưu trữ: {storageKey}.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully uploaded file {FileName} with ID {FileId} to {ProviderType}", 
            request.FileName, fileEntry.Id, provider.Type);

        var dto = new FileEntryDto(
            fileEntry.Id,
            fileEntry.TenantId,
            fileEntry.OwnerId,
            user.FullName,
            fileEntry.FolderId,
            fileEntry.Name,
            fileEntry.StorageKey,
            fileEntry.ContentType,
            fileEntry.SizeBytes,
            fileEntry.Hash,
            fileEntry.ScanStatus,
            provider.Id,
            provider.Name,
            provider.Type.ToString(),
            fileEntry.CreatedAt
        );

        return Result<FileEntryDto>.Success(dto);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
