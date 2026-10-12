using System.Data;
using System.Security.Cryptography;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
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
        // 0. Xác định User & Tenant Context từ JWT Token (không cho phép impersonation / fallback)
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Result<FileEntryDto>.Failure("Chưa xác thực danh tính người dùng.");
        }

        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        if (user == null)
        {
            return Result<FileEntryDto>.Failure("Không tìm thấy thông tin tài khoản người dùng.");
        }

        // Kiểm tra vai trò người dùng:
        var roleNames = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role.Name)
            .ToList();

        bool isSystemAdmin = roleNames.Contains("SystemAdmin") || _currentUserService.Role == "SystemAdmin";
        if (isSystemAdmin)
        {
            return Result<FileEntryDto>.Failure("Tài khoản Quản trị viên hệ thống (SystemAdmin) không thuộc tổ chức nào nên không có quyền tải lên tệp tin.");
        }

        bool isTenantAdmin = roleNames.Contains("TenantAdmin");
        bool isDeptAdmin = roleNames.Contains("DepartmentAdmin");

        var tenantId = _currentUserService.TenantId ?? user.TenantId;
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

        var provider = tenant.StorageProvider;
        if (provider == null)
        {
            return Result<FileEntryDto>.Failure(
                $"Tổ chức '{tenant.Name}' chưa có Nhà cung cấp lưu trữ (Storage Provider). Vui lòng liên hệ Quản trị viên hệ thống để cấp cấu hình kho lưu trữ.");
        }

        // Xử lý Phòng ban (Department) cho người upload
        if (!user.DepartmentId.HasValue)
        {
            return Result<FileEntryDto>.Failure("Tài khoản người dùng chưa được phân bổ vào bất kỳ Phòng ban nào.");
        }

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == user.DepartmentId.Value && d.TenantId == tenant.Id, cancellationToken);

        if (department == null)
        {
            return Result<FileEntryDto>.Failure("Phòng ban của người dùng không tồn tại trong tổ chức.");
        }

        if (!department.IsActive)
        {
            return Result<FileEntryDto>.Failure($"Phòng ban '{department.Name}' hiện đang bị vô hiệu hóa.");
        }

        // BƯỚC 1: Pre-check Hạn mức lưu trữ cá nhân & phòng ban
        var userQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == userId.Value, cancellationToken);

        if (userQuota == null || userQuota.QuotaBytes <= 0)
        {
            return Result<FileEntryDto>.Failure(
                "Bạn chưa được cấp hạn mức lưu trữ cá nhân (Quota = 0). Vui lòng liên hệ Quản trị viên phòng ban để được phân bổ dung lượng trước khi tải lên tệp tin.");
        }

        // Kiểm tra dung lượng cá nhân
        if (userQuota.UsedBytes + request.FileSize > userQuota.QuotaBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Bạn đã sử dụng hết hạn mức lưu trữ cá nhân! (Đã dùng: {FormatBytes(userQuota.UsedBytes)} / Hạn mức: {FormatBytes(userQuota.QuotaBytes)}).");
        }

        // Kiểm tra dung lượng phòng ban
        if (department.StorageQuotaBytes > 0 && department.StorageUsedBytes + request.FileSize > department.StorageQuotaBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Phòng ban '{department.Name}' đã sử dụng hết hạn mức lưu trữ được cấp! (Đã dùng: {FormatBytes(department.StorageUsedBytes)} / Hạn mức: {FormatBytes(department.StorageQuotaBytes)}).");
        }

        // Kiểm tra dung lượng Storage Provider
        if (provider.TotalCapacityBytes > 0 && provider.UsedCapacityBytes + request.FileSize > provider.TotalCapacityBytes)
        {
            return Result<FileEntryDto>.Failure(
                $"Kho lưu trữ '{provider.Name}' ({provider.Type}) đã đầy dung lượng!");
        }

        // Kiểm tra Thư mục đích (Folder Scope)
        if (request.FolderId.HasValue)
        {
            var targetFolder = await _context.Folders
                .FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && f.TenantId == tenant.Id, cancellationToken);

            if (targetFolder == null)
            {
                return Result<FileEntryDto>.Failure("Thư mục đích không tồn tại hoặc không thuộc tổ chức của bạn.");
            }

            if (targetFolder.DepartmentId.HasValue && targetFolder.DepartmentId.Value != department.Id)
            {
                return Result<FileEntryDto>.Failure("Bạn không có quyền tải lên tệp tin vào thư mục của phòng ban khác.");
            }
        }

        // BƯỚC 2: Quét Virus (ClamAV)
        _logger.LogInformation("Scanning file {FileName} ({SizeBytes} bytes) with ClamAV...", request.FileName, request.FileSize);
        var scanResult = await _virusScannerService.ScanStreamAsync(request.FileStream, cancellationToken);

        if (scanResult.IsInfected)
        {
            _logger.LogWarning("Malware detected in upload: {ThreatName}", scanResult.ThreatName);

            var malwareAuditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = userId.Value,
                Action = "VirusDetected",
                EntityName = "FileEntry",
                Details = $"CẢNH BÁO BẢO MẬT: Phát hiện mã độc '{scanResult.ThreatName}' trong tệp tin '{request.FileName}' của Phòng ban '{department.Name}'. Đã chặn tải lên.",
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

        // BƯỚC 4: Định tuyến và Stream Upload lên Cloud Provider (Network I/O - Không giữ bất kỳ DB lock nào)
        _logger.LogInformation("Routing upload to provider: {ProviderName} ({ProviderType})", provider.Name, provider.Type);
        var storageService = _storageServiceFactory.CreateService(provider);

        string? destinationFolderId = request.FolderId.HasValue 
            ? request.FolderId.Value.ToString() 
            : (provider.Type == StorageProviderType.Local ? null : provider.RootFolderId);

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

        // BƯỚC 5: Atomic Verification & DB Persistence trong Transaction Serializable (chống Race Condition)
        // Nếu lúc này phát hiện quota bị vượt do request đồng thời hoàn tất trước -> Rollback & Xóa bù trừ file trên Cloud
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            try
            {
                // Re-fetch Quota và Provider trong Transaction để kiểm tra giá trị thực sự mới nhất
                var lockedQuota = await _context.UserStorageQuotas
                    .FirstOrDefaultAsync(q => q.UserId == userId.Value, cancellationToken);

                if (lockedQuota == null || lockedQuota.QuotaBytes <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    await TryCleanupCloudFileAsync(storageService, storageKey);
                    return Result<FileEntryDto>.Failure(
                        "Bạn chưa được cấp hạn mức lưu trữ cá nhân (Quota = 0).");
                }

                if (lockedQuota.UsedBytes + request.FileSize > lockedQuota.QuotaBytes)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    await TryCleanupCloudFileAsync(storageService, storageKey);
                    return Result<FileEntryDto>.Failure(
                        $"Dung lượng lưu trữ cá nhân đã bị vượt do có tệp tin khác tải lên đồng thời! (Đã dùng: {FormatBytes(lockedQuota.UsedBytes)} / Hạn mức: {FormatBytes(lockedQuota.QuotaBytes)}).");
                }

                var lockedProvider = await _context.StorageProviders
                    .FirstOrDefaultAsync(p => p.Id == provider.Id, cancellationToken);

                if (lockedProvider != null && lockedProvider.TotalCapacityBytes > 0 &&
                    lockedProvider.UsedCapacityBytes + request.FileSize > lockedProvider.TotalCapacityBytes)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    await TryCleanupCloudFileAsync(storageService, storageKey);
                    return Result<FileEntryDto>.Failure(
                        $"Kho lưu trữ '{lockedProvider.Name}' đã đầy dung lượng do có tệp tin khác tải lên đồng thời!");
                }

                var lockedDept = await _context.Departments
                    .FirstOrDefaultAsync(d => d.Id == department.Id, cancellationToken);

                if (lockedDept != null && lockedDept.StorageQuotaBytes > 0 &&
                    lockedDept.StorageUsedBytes + request.FileSize > lockedDept.StorageQuotaBytes)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    await TryCleanupCloudFileAsync(storageService, storageKey);
                    return Result<FileEntryDto>.Failure(
                        $"Dung lượng phòng ban '{lockedDept.Name}' đã bị vượt do có tệp tin khác tải lên đồng thời!");
                }

                var lockedTenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == tenant.Id, cancellationToken);

                // Tạo thực thể FileEntry
                var fileEntry = new FileEntry
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    DepartmentId = department.Id,
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

                // Cập nhật dung lượng
                if (lockedDept != null) lockedDept.StorageUsedBytes += request.FileSize;
                if (lockedQuota != null) lockedQuota.UsedBytes += request.FileSize;
                if (lockedTenant != null) lockedTenant.StorageUsedBytes += request.FileSize;
                if (lockedProvider != null) lockedProvider.UsedCapacityBytes += request.FileSize;

                // Ghi AuditLog
                string roleTitle = isDeptAdmin ? "Trưởng phòng/Admin Phòng ban" : (isTenantAdmin ? "Quản trị viên Tổ chức" : "Thành viên");
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = userId.Value,
                    Action = "Upload",
                    EntityName = "FileEntry",
                    EntityId = fileEntry.Id,
                    Details = $"[{roleTitle} - {department.Name}] {user.FullName} đã tải lên thành công '{request.FileName}' ({FormatBytes(request.FileSize)}) vào {provider.Name} ({provider.Type}). Khóa lưu trữ: {storageKey}.",
                    CreatedAt = DateTime.UtcNow
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Successfully uploaded file {FileName} with ID {FileId} to {ProviderType} under Serializable transaction", 
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
                    fileEntry.CreatedAt,
                    department.Id,
                    department.Name
                );

                return Result<FileEntryDto>.Success(dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                await TryCleanupCloudFileAsync(storageService, storageKey);
                _logger.LogError(ex, "Transaction failed while persisting uploaded file {FileName}. Performed cloud file compensation cleanup.", request.FileName);
                throw;
            }
        });
    }

    private async Task TryCleanupCloudFileAsync(IStorageService storageService, string storageKey)
    {
        try
        {
            await storageService.DeleteFileAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up cloud storage key {StorageKey} during rollback compensation", storageKey);
        }
    }
}
