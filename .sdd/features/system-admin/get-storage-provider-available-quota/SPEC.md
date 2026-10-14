# SPEC: Get Storage Provider Available Quota (Lấy Hạn mức Khả dụng của Provider)

- **Feature Slug:** `get-storage-provider-available-quota`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Tính toán hạn mức dung lượng còn lại có thể phân bổ cho các Tenant của một Storage Provider cụ thể.
  - Risk Posture: `LOW` (Read-only mathematical projection query).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Giúp `SystemAdmin` nắm được chính xác dung lượng còn lại chưa cam kết của một Storage Provider trước khi tiến hành phân bổ hạn mức cho một Tenant mới hoặc mở rộng hạn mức cho Tenant hiện tại.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.View`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi truy vấn `GetStorageProviderAvailableQuotaQuery` với `ProviderId` hợp lệ, `THEN` hệ thống `SHALL` tính toán công thức:
  - `CloudFreeBytes = Max(0, TotalCapacityBytes - UsedCapacityBytes)`
  - `AlreadyAllocatedBytes = Sum(Tenant.StorageQuotaBytes)`
  - `AvailableToAllocateBytes = Max(0, CloudFreeBytes - AlreadyAllocatedBytes)`
  và trả về `StorageProviderAvailableQuotaDto`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy Provider theo `ProviderId`, `THEN` hệ thống `SHALL` trả về `null`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Thời gian xử lý truy vấn phải dưới **50ms**.

---

## 5. Data Model & Error Contract
- **Query:** `GetStorageProviderAvailableQuotaQuery(ProviderId)`
- **Response:** `StorageProviderAvailableQuotaDto?`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Tính toán dung lượng khả dụng của Storage Provider
  Given Provider có tổng 100 GB, Cloud đã dùng 20 GB (trống 80 GB)
  And đã cấp phát cho Tenant A 30 GB, Tenant B 20 GB (tổng cấp phát 50 GB)
  When SystemAdmin gọi GetStorageProviderAvailableQuotaQuery
  Then AvailableToAllocateBytes trả về bằng 30 GB (80 GB - 50 GB)
```

---

## 7. Out of Scope
- Tự động thay đổi hạn mức của Tenant khi dung lượng khả dụng giảm.

---

## 8. Feature Lock
- **Locked Scope:** Công thức tính toán và trả về dung lượng khả dụng để phân bổ.
- **Change Path:** `/sdd-update --feature=get-storage-provider-available-quota --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-storage-provider-available-quota` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-storage-provider-available-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
