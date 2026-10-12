-- =========================================================================
-- Script SQL Đối Soát & Tái Tính Toán Dung Lượng 4 Bảng (Reconciliation Script)
-- Sử dụng khi cần đồng bộ lại UsedBytes thực tế từ bảng FileEntries vào:
-- 1. Tenants (StorageUsedBytes)
-- 2. Departments (StorageUsedBytes)
-- 3. UserStorageQuotas (UsedBytes)
-- 4. StorageProviders (UsedCapacityBytes)
-- =========================================================================

-- 1. Cập nhật lại StorageUsedBytes của bảng Tenants
UPDATE `Tenants` t
LEFT JOIN (
    SELECT `TenantId`, COALESCE(SUM(`SizeBytes`), 0) AS `ActualUsed`
    FROM `FileEntries`
    WHERE `IsDeleted` = 0
    GROUP BY `TenantId`
) f ON t.`Id` = f.`TenantId`
SET t.`StorageUsedBytes` = COALESCE(f.`ActualUsed`, 0);

-- 2. Cập nhật lại StorageUsedBytes của bảng Departments
UPDATE `Departments` d
LEFT JOIN (
    SELECT `DepartmentId`, COALESCE(SUM(`SizeBytes`), 0) AS `ActualUsed`
    FROM `FileEntries`
    WHERE `IsDeleted` = 0 AND `DepartmentId` IS NOT NULL
    GROUP BY `DepartmentId`
) f ON d.`Id` = f.`DepartmentId`
SET d.`StorageUsedBytes` = COALESCE(f.`ActualUsed`, 0);

-- 3. Cập nhật lại UsedBytes của bảng UserStorageQuotas
UPDATE `UserStorageQuotas` uq
LEFT JOIN (
    SELECT `OwnerId`, COALESCE(SUM(`SizeBytes`), 0) AS `ActualUsed`
    FROM `FileEntries`
    WHERE `IsDeleted` = 0
    GROUP BY `OwnerId`
) f ON uq.`UserId` = f.`OwnerId`
SET uq.`UsedBytes` = COALESCE(f.`ActualUsed`, 0);

-- 4. Cập nhật lại UsedCapacityBytes của bảng StorageProviders
UPDATE `StorageProviders` sp
LEFT JOIN (
    SELECT `StorageProviderId`, COALESCE(SUM(`SizeBytes`), 0) AS `ActualUsed`
    FROM `FileEntries`
    WHERE `IsDeleted` = 0 AND `StorageProviderId` IS NOT NULL
    GROUP BY `StorageProviderId`
) f ON sp.`Id` = f.`StorageProviderId`
SET sp.`UsedCapacityBytes` = COALESCE(f.`ActualUsed`, 0);

-- 5. Query đối soát kiểm tra chênh lệch
SELECT 
    'Tenants' AS Entity,
    t.Id,
    t.Name,
    t.StorageUsedBytes AS RecordedBytes,
    COALESCE(actual.Bytes, 0) AS ActualBytes,
    (t.StorageUsedBytes - COALESCE(actual.Bytes, 0)) AS Diff
FROM Tenants t
LEFT JOIN (
    SELECT TenantId, SUM(SizeBytes) AS Bytes 
    FROM FileEntries WHERE IsDeleted = 0 GROUP BY TenantId
) actual ON t.Id = actual.TenantId
WHERE t.StorageUsedBytes != COALESCE(actual.Bytes, 0);
