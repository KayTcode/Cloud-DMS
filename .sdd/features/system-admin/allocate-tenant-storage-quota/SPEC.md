# SPEC: Allocate Tenant Storage Quota (Cấp phát Hạn mức Lưu trữ cho Tenant)

- **Feature Slug:** `allocate-tenant-storage-quota`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Cấp phát và điều phối dung lượng giữa các Tenant, kiểm soát tài nguyên tránh tình trạng cạn kiệt lưu trữ của Cloud Provider.
  - Risk Posture: `HIGH` (Toàn vẹn hạn mức lưu trữ đa tổ chức).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` liên kết một Tenant với một `StorageProvider` cụ thể và ấn định hạn mức lưu trữ (`StorageQuotaBytes`). Đảm bảo hai điều kiện biên quan trọng:
1. Không được cấp phát vượt quá dung lượng khả dụng của Provider.
2. Không được hạ hạn mức thấp hơn dung lượng thực tế Tenant đã sử dụng (`StorageUsedBytes`).

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.Manage`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `AllocateTenantStorageQuotaCommand` với hạn mức hợp lệ, `THEN` hệ thống `SHALL` cập nhật `tenant.StorageProviderId` và `tenant.StorageQuotaBytes`, lưu CSDL và trả về `TenantStorageQuotaDto`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy Tenant theo `TenantId`, `THEN` hệ thống `SHALL` ném ngoại lệ `KeyNotFoundException("Không tìm thấy Tenant với ID {request.TenantId}")`.
- **EARS-03 (Unwanted):** `IF` không tìm thấy Cloud Provider theo `StorageProviderId`, `THEN` hệ thống `SHALL` ném ngoại lệ `KeyNotFoundException("Không tìm thấy Cloud Provider với ID {request.StorageProviderId}")`.
- **EARS-04 (Unwanted):** `IF` Cloud Provider đang bị vô hiệu hóa (`IsActive = false`), `THEN` hệ thống `SHALL` ném ngoại lệ `InvalidOperationException("Cloud Provider '{provider.Name}' hiện đang bị vô hiệu hóa.")`.
- **EARS-05 (Unwanted):** `IF` `QuotaBytes` yêu cầu vượt quá dung lượng khả dụng chưa phân bổ của Provider (`availableForThisTenant`), `THEN` hệ thống `SHALL` ném ngoại lệ `InvalidOperationException("Vượt quá dung lượng khả dụng của Cloud Provider! Bạn yêu cầu {requestedGB} GB nhưng Cloud chỉ còn {availableGB} GB.")`.
- **EARS-06 (Unwanted):** `IF` `QuotaBytes` yêu cầu nhỏ hơn dung lượng Tenant đã dùng (`tenant.StorageUsedBytes`), `THEN` hệ thống `SHALL` ném ngoại lệ `InvalidOperationException("Không thể cấp quota nhỏ hơn dung lượng mà Tenant đã sử dụng ({usedGB} GB).")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-RELIABILITY-01:** Bảo đảm tính toàn vẹn: Tổng hạn mức phân bổ cho các Tenant của 1 Provider không được vượt quá dung lượng đĩa thực tế của Provider đó.

---

## 5. Data Model & Error Contract
- **Command:** `AllocateTenantStorageQuotaCommand(TenantId, StorageProviderId, QuotaBytes)`
- **Response:** `TenantStorageQuotaDto`
- **Exceptions:**
  - `KeyNotFoundException`: Tenant hoặc Provider không tồn tại.
  - `InvalidOperationException`: Provider bị tắt, Quota vượt quá dung lượng trống, hoặc Quota nhỏ hơn dung lượng đã dùng.

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Cấp phát hạn mức lưu trữ cho Tenant thành công
  Given Tenant "Tenant-A" đang dùng 5 GB
  And Provider "Google Drive" còn khả dụng 40 GB
  When SystemAdmin cấp phát 20 GB cho "Tenant-A"
  Then StorageQuotaBytes của Tenant-A được cập nhật thành 20 GB
  And phản hồi TenantStorageQuotaDto chính xác
```

---

## 7. Out of Scope
- Tự động di chuyển dữ liệu giữa các Provider khi Tenant được đổi sang Provider khác.

---

## 8. Feature Lock
- **Locked Scope:** Kiểm tra ràng buộc dung lượng và gán quota cho Tenant.
- **Change Path:** `/sdd-update --feature=allocate-tenant-storage-quota --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `allocate-tenant-storage-quota` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature allocate-tenant-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
