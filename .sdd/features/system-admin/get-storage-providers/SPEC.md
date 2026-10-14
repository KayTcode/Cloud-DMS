# SPEC: Get Storage Providers (Lấy Danh sách Storage Providers)

- **Feature Slug:** `get-storage-providers`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vấn và thống kê tổng hợp dung lượng các nhà cung cấp lưu trữ toàn hệ thống.
  - Risk Posture: `LOW` (Read-only aggregation query).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cung cấp cho `SystemAdmin` bức tranh toàn cảnh về hạ tầng lưu trữ của hệ thống: Danh sách các nhà cung cấp (Local, Google Drive, OneDrive), tổng dung lượng đĩa, dung lượng thực tế đã tiêu hao trên Cloud, dung lượng đã cam kết cấp phát cho các Tenant (`AllocatedToTenantsBytes`), và số lượng Tenant đang sử dụng từng Provider.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.View`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi truy vấn `GetStorageProvidersQuery`, `THEN` hệ thống `SHALL` nạp tất cả các bản ghi `StorageProviders` kèm danh sách `Tenants` liên kết, tính toán tổng dung lượng đã cấp phát (`Sum(t.StorageQuotaBytes)`), đếm số lượng Tenant và trả về danh sách `List<StorageProviderDto>`.
- **EARS-02 (State-driven):** `WHILE` một Provider chưa có dung lượng tổng (`TotalCapacityBytes <= 0`), hệ thống `SHALL` sử dụng dung lượng mặc định 15 GB để phục vụ tính toán hiển thị.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Truy vấn danh sách và tính toán thống kê phải hoàn thành trong **< 100ms**.

---

## 5. Data Model & Error Contract
- **Query:** `GetStorageProvidersQuery`
- **Response:** `List<StorageProviderDto>`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Lấy danh sách Storage Providers thành công
  Given hệ thống có 2 Storage Provider: "Google Drive" và "Local Storage"
  When SystemAdmin gửi GetStorageProvidersQuery
  Then hệ thống trả về danh sách gồm 2 StorageProviderDto
  And mỗi DTO có AllocatedToTenantsBytes và TenantsCount chính xác
```

---

## 7. Out of Scope
- Tự động gọi API đo dung lượng Cloud thời gian thực ở mỗi lần query (chỉ đọc số liệu đã sync gần nhất trong DB).

---

## 8. Feature Lock
- **Locked Scope:** Truy vấn danh sách và tổng hợp chỉ số Storage Providers.
- **Change Path:** `/sdd-update --feature=get-storage-providers --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-storage-providers` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-storage-providers.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
