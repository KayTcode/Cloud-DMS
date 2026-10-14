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

        Guid? currentTenantId = _currentUserService.TenantId;

        // BẢO MẬT: Chống giả mạo UserId
        if (request.UserIdOverride.HasValue && request.UserIdOverride != currentUserId)
        {
            _logger.LogWarning("Security Alert: User {UserId} attempted unauthorized UserIdOverride to {OverrideId}", currentUserId, request.UserIdOverride);
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = currentTenantId ?? Guid.Empty,
                UserId = currentUserId.Value,
                Action = "SecurityTamperingAttempt",
                EntityName = "FileEntry",
                Details = $"CẢNH BÁO BẢO MẬT: Người dùng cố gắng thay đổi UserId thành '{request.UserIdOverride}' khi tải lên tệp tin.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
            return Result<FileEntryDto>.Failure("Không được phép thay đổi người dùng tải lên.");
        }

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

        // BẢO MẬT: Chống giả mạo TenantId
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride != tenantId)
        {
            _logger.LogWarning("Security Alert: User {UserId} attempted unauthorized TenantIdOverride to {OverrideTenantId}", currentUserId, request.TenantIdOverride);
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                UserId = currentUserId.Value,
                Action = "SecurityTamperingAttempt",
                EntityName = "FileEntry",
                Details = $"CẢNH BÁO BẢO MẬT: Người dùng cố gắng thay đổi TenantId thành '{request.TenantIdOverride}' khi tải lên tệp tin.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
            return Result<FileEntryDto>.Failure("Không được phép thay đổi tenant tải lên.");
        }

        // BẢO MẬT: Chống giả mạo StorageProviderId
        if (request.StorageProviderIdOverride.HasValue && request.StorageProviderIdOverride != (user.Tenant?.StorageProviderId ?? Guid.Empty))
        {
            _logger.LogWarning("Security Alert: User {UserId} attempted unauthorized StorageProviderIdOverride to {OverrideProviderId}", currentUserId, request.StorageProviderIdOverride);
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                UserId = currentUserId.Value,
                Action = "SecurityTamperingAttempt",
                EntityName = "FileEntry",
                Details = $"CẢNH BÁO BẢO MẬT: Người dùng cố gắng thay đổi StorageProviderId thành '{request.StorageProviderIdOverride}' khi tải lên tệp tin.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
            return Result<FileEntryDto>.Failure("Không được phép thay đổi nhà cung cấp lưu trữ.");
        }

        Guid userId = currentUserId.Value;

        // Kiểm tra Tenant còn hoạt động (IsActive)
        var tenant = await _context.Tenants
            .Include(t => t.StorageProvider)
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value && t.IsActive, cancellationToken);

        if (tenant == null)
        {
            return Result<FileEntryDto>.Failure("Tổ chức/Tenant không tồn tại hoặc đã bị khóa/vô hiệu hóa.");
        }

        // BẢO MẬT (Anti-BOLA/IDOR): Xác thực thư mục đích nếu có chỉ định
        if (request.FolderId.HasValue)
        {
            var targetFolder = await _context.Folders
                .FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && f.TenantId == tenant.Id, cancellationToken);

            if (targetFolder == null)
            {
                return Result<FileEntryDto>.Failure("Thư mục đích không tồn tại hoặc bạn không có quyền truy cập.");
            }

            // Kiểm tra phân quyền phòng ban cho Employee hoặc DepartmentAdmin
            var userRole = _currentUserService.Role;
            if (userRole == "Employee" || userRole == "DepartmentAdmin")
            {
                if (user.DepartmentId.HasValue && targetFolder.DepartmentId.HasValue && targetFolder.DepartmentId != user.DepartmentId)
                {
                    return Result<FileEntryDto>.Failure("Thư mục đích không thuộc quyền quản lý của phòng ban bạn.");
                }
            }
        }

        // BƯỚC 1: Kiểm tra sơ bộ Quota trước khi upload
        // 1.1 Kiểm tra Quota của Tenant
        if (tenant.StorageUsedBytes + request.FileSize > tenant.StorageQuotaBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Hết dung lượng lưu trữ của Tenant! (Đã dùng: {FormatBytes(tenant.StorageUsedBytes)} / Hạn mức: {FormatBytes(tenant.StorageQuotaBytes)}).");
        }

        // 1.2 Kiểm tra Quota cá nhân User (nếu có cấp riêng)
        var userQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == userId, cancellationToken);

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

        if (provider == null || !provider.IsActive)
        {
            return Result<FileEntryDto>.Failure(
                $"Tổ chức '{tenant.Name}' chưa có Nhà cung cấp lưu trữ hợp lệ hoặc Provider đã bị vô hiệu hóa. Vui lòng liên hệ Quản trị viên hệ thống!");
        }

        if (provider.TotalCapacityBytes > 0 && provider.UsedCapacityBytes + request.FileSize > provider.TotalCapacityBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Kho lưu trữ đám mây '{provider.Name}' ({provider.Type}) đã đầy dung lượng!");
        }

        // Làm sạch tên tệp tin chống Path Traversal và ký tự đặc biệt
        var safeFileName = Path.GetFileName(request.FileName);
        safeFileName = string.Join("_", safeFileName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = "unnamed_file";
        }

        // BƯỚC 2: Quét Virus (ClamAV)
        _logger.LogInformation("Scanning file {FileName} ({SizeBytes} bytes) with ClamAV...", safeFileName, request.FileSize);
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
                Details = $"CẢNH BÁO BẢO MẬT: Phát hiện mã độc '{scanResult.ThreatName}' trong tệp tin '{safeFileName}'. Đã chặn tải lên.",
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
                safeFileName,
                request.ContentType,
                destinationFolderId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to cloud provider {ProviderName}", provider.Name);
            return Result<FileEntryDto>.Failure($"Lỗi khi tải tệp tin lên nhà cung cấp lưu trữ ({provider.Name}): {ex.Message}");
        }

        // BƯỚC 5: Cập nhật CSDL trong Transaction an toàn & Bồi hoàn nếu thất bại
        var fileEntry = new FileEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DepartmentId = user.DepartmentId,
            OwningDepartmentId = user.DepartmentId,
            OwnerId = userId,
            FolderId = request.FolderId,
            Name = safeFileName,
            StorageKey = storageKey,
            ContentType = request.ContentType,
            SizeBytes = request.FileSize,
            Hash = hashHex,
            ScanStatus = "Clean",
            StorageProviderId = provider.Id,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId,
            Action = "Upload",
            EntityName = "FileEntry",
            EntityId = fileEntry.Id,
            Details = $"Tải lên thành công '{safeFileName}' ({FormatBytes(request.FileSize)}) vào {provider.Name} ({provider.Type}). Khóa lưu trữ: {storageKey}.",
            CreatedAt = DateTime.UtcNow
        };

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

        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                try
                {
                    // Kiểm tra lại Quota trong transaction Serializable để chống TOCTOU race condition
                    var lockedTenant = await _context.Tenants
                        .FirstOrDefaultAsync(t => t.Id == tenant.Id, cancellationToken);
                    if (lockedTenant == null || !lockedTenant.IsActive)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException("Tổ chức/Tenant không tồn tại hoặc đã bị khóa.");
                    }

                    if (lockedTenant.StorageUsedBytes + request.FileSize > lockedTenant.StorageQuotaBytes)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException($"Hết dung lượng lưu trữ của Tenant! (Đã dùng: {FormatBytes(lockedTenant.StorageUsedBytes)} / Hạn mức: {FormatBytes(lockedTenant.StorageQuotaBytes)}).");
                    }

                    UserStorageQuota? lockedUserQuota = null;
                    if (userQuota != null)
                    {
                        lockedUserQuota = await _context.UserStorageQuotas
                            .FirstOrDefaultAsync(q => q.UserId == userId, cancellationToken);
                        if (lockedUserQuota != null && lockedUserQuota.UsedBytes + request.FileSize > lockedUserQuota.QuotaBytes)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            throw new InvalidOperationException($"Hết dung lượng cá nhân được phân bổ! (Đã dùng: {FormatBytes(lockedUserQuota.UsedBytes)} / Hạn mức: {FormatBytes(lockedUserQuota.QuotaBytes)}).");
                        }
                    }

                    // Cập nhật dung lượng sử dụng
                    lockedTenant.StorageUsedBytes += request.FileSize;

                    if (user.DepartmentId.HasValue)
                    {
                        var dept = await _context.Departments
                            .FirstOrDefaultAsync(d => d.Id == user.DepartmentId.Value, cancellationToken);
                        if (dept != null)
                        {
                            dept.StorageUsedBytes += request.FileSize;
                        }
                    }

                    var lockedProvider = await _context.StorageProviders
                        .FirstOrDefaultAsync(p => p.Id == provider.Id, cancellationToken);
                    if (lockedProvider != null)
                    {
                        lockedProvider.UsedCapacityBytes += request.FileSize;
                    }

                    if (lockedUserQuota != null)
                    {
                        lockedUserQuota.UsedBytes += request.FileSize;
                    }

                    _context.FileEntries.Add(fileEntry);
                    _context.AuditLogs.Add(auditLog);

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation("Successfully uploaded file {FileName} with ID {FileId} to {ProviderType}",
                        safeFileName, fileEntry.Id, provider.Type);

                    return Result<FileEntryDto>.Success(dto);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    throw;
                }
            });
        }
        catch (Exception dbEx)
        {
            // BỒI HOÀN: Xóa file vừa upload trên Cloud Storage Provider để chống Ghost File Leak
            _logger.LogError(dbEx, "Lỗi khi ghi nhận dữ liệu CSDL sau khi upload file {FileName} lên Cloud. Bắt đầu bồi hoàn (xóa file {StorageKey})...", safeFileName, storageKey);
            try
            {
                await storageService.DeleteFileAsync(storageKey, CancellationToken.None);
                _logger.LogInformation("Bồi hoàn thành công: Đã xóa tệp tin mồ côi {StorageKey} trên Cloud Provider.", storageKey);
            }
            catch (Exception deleteEx)
            {
                _logger.LogCritical(deleteEx, "CẢNH BÁO BỒI HOÀN THẤT BẠI: Không thể xóa tệp tin mồ côi {StorageKey} trên Cloud Provider!", storageKey);
            }

            return Result<FileEntryDto>.Failure($"Lỗi khi ghi nhận thông tin tệp tin vào cơ sở dữ liệu: {dbEx.Message}");
        }
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
