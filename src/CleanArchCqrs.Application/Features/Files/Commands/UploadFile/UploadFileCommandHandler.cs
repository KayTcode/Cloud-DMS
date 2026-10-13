using System.Data;
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
        Guid? currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            return Result<FileEntryDto>.Failure("Yêu cầu không hợp lệ: Chưa xác thực danh tính người dùng.");
        }

        if (request.UserIdOverride.HasValue && request.UserIdOverride != currentUserId)
        {
            return Result<FileEntryDto>.Failure("Không được phép thay đổi người dùng tải lên.");
        }

        Guid? currentTenantId = _currentUserService.TenantId;
        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == currentUserId.Value && u.IsActive, cancellationToken);

        if (user == null)
        {
            return Result<FileEntryDto>.Failure("Người dùng hiện tại không hợp lệ hoặc đã bị vô hiệu hóa.");
        }

        Guid? tenantId = currentTenantId ?? user.TenantId;
        if (!tenantId.HasValue)
        {
            return Result<FileEntryDto>.Failure("Người dùng hiện tại chưa được liên kết với bất kỳ Tổ chức/Tenant nào.");
        }

        if (request.TenantIdOverride.HasValue && request.TenantIdOverride != tenantId)
        {
            return Result<FileEntryDto>.Failure("Không được phép thay đổi tenant tải lên.");
        }

        if (request.StorageProviderIdOverride.HasValue && request.StorageProviderIdOverride != (user.Tenant?.StorageProviderId ?? Guid.Empty))
        {
            return Result<FileEntryDto>.Failure("Không được phép thay đổi nhà cung cấp lưu trữ.");
        }

        Guid userId = currentUserId.Value;

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
            .FirstOrDefaultAsync(q => q.UserId == userId, cancellationToken);

        if (userQuota is null)
        {
            return Result<FileEntryDto>.Failure("Người dùng chưa được cấp hạn mức lưu trữ cá nhân.");
        }

        if (userQuota.UsedBytes + request.FileSize > userQuota.QuotaBytes)
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
                UserId = userId,
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
            OwnerId = userId,
            OwningDepartmentId = user.DepartmentId,
            DepartmentId = user.DepartmentId,
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

        // All writers use SQL deltas against the current row. The quota reads above
        // are advisory because scanning and object storage can take arbitrarily long.
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var tenantUpdated = await _context.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenant.Id && t.StorageUsedBytes <= t.StorageQuotaBytes - request.FileSize)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes,
                t => t.StorageUsedBytes + request.FileSize), cancellationToken);
        if (tenantUpdated != 1)
            return Result<FileEntryDto>.Failure("Hết dung lượng lưu trữ của Tenant.");

        if (user.DepartmentId.HasValue)
        {
            var departmentUpdated = await _context.Departments.IgnoreQueryFilters()
                .Where(d => d.Id == user.DepartmentId && d.TenantId == tenant.Id
                    && d.UsedBytes <= d.QuotaBytes - request.FileSize)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedBytes,
                    d => d.UsedBytes + request.FileSize), cancellationToken);
            if (departmentUpdated != 1)
                return Result<FileEntryDto>.Failure("Hết dung lượng lưu trữ của phòng ban.");
        }

        var userUpdated = await _context.UserStorageQuotas.IgnoreQueryFilters()
            .Where(q => q.Id == userQuota.Id && q.UserId == userId
                && q.UsedBytes <= q.QuotaBytes - request.FileSize)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes,
                q => q.UsedBytes + request.FileSize), cancellationToken);
        if (userUpdated != 1)
            return Result<FileEntryDto>.Failure("Hết dung lượng cá nhân được phân bổ.");

        var providerUpdated = await _context.StorageProviders.IgnoreQueryFilters()
            .Where(p => p.Id == provider.Id && (p.TotalCapacityBytes <= 0
                || p.UsedCapacityBytes <= p.TotalCapacityBytes - request.FileSize))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCapacityBytes,
                p => p.UsedCapacityBytes + request.FileSize), cancellationToken);
        if (providerUpdated != 1)
            return Result<FileEntryDto>.Failure("Kho lưu trữ đã đầy dung lượng.");

        _context.FileEntries.Add(fileEntry);

        // Ghi AuditLog
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId,
            Action = "Upload",
            EntityName = "FileEntry",
            EntityId = fileEntry.Id,
            Details = $"Tải lên thành công '{request.FileName}' ({FormatBytes(request.FileSize)}) vào {provider.Name} ({provider.Type}). Khóa lưu trữ: {storageKey}.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
