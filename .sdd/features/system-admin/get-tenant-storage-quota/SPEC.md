# SPEC: Get Tenant Storage Quota (Lấy Hạn mức Lưu trữ của Tenant)

- **Feature Slug:** `get-tenant-storage-quota`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vấn thông tin hạn mức và mức tiêu thụ tài nguyên thực tế của một Tenant.
  - Risk Posture: `LOW` (Read-only single tenant query).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cho phép `SystemAdmin` tra cứu thông tin chi tiết về hạ tầng lưu trữ của một Tenant cụ thể: Tên/Mã Tenant, Cloud Storage Provider đang liên kết, hạn mức được cấp (`StorageQuotaBytes`) và dung lượng tệp tin thực tế mà Tenant đã tiêu hao (`StorageUsedBytes`).

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.View`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi truy vấn `GetTenantStorageQuotaQuery` với `TenantId` hợp lệ, `THEN` hệ thống `SHALL` nạp thông tin Tenant kèm `StorageProvider` liên kết và trả về `TenantStorageQuotaDto`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy Tenant theo `TenantId`, `THEN` hệ thống `SHALL` trả về `null`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Thời gian xử lý truy vấn phải dưới **50ms**.

---

## 5. Data Model & Error Contract
- **Query:** `GetTenantStorageQuotaQuery(TenantId)`
- **Response:** `TenantStorageQuotaDto?`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Tra cứu hạn mức của Tenant thành công
  Given Tenant "Tenant-1" được cấp 10 GB và đã sử dụng 2 GB trên "Local Disk"
  When SystemAdmin gọi GetTenantStorageQuotaQuery cho "Tenant-1"
  Then hệ thống trả về TenantStorageQuotaDto với StorageQuotaBytes = 10 GB và StorageUsedBytes = 2 GB
```

---

## 7. Out of Scope
- Liệt kê chi tiết danh sách từng file gây tiêu tốn dung lượng của Tenant.

---

## 8. Feature Lock
- **Locked Scope:** Truy vấn hạn mức và dung lượng đã dùng của Tenant.
- **Change Path:** `/sdd-update --feature=get-tenant-storage-quota --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-tenant-storage-quota` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-tenant-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
