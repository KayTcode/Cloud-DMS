# SPEC: Delete Storage Provider (Xóa Nhà cung cấp Lưu trữ)

- **Feature Slug:** `delete-storage-provider`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Gỡ bỏ cấu hình Storage Provider, tự động ngắt liên kết của các Tenant đang trỏ tới Provider để tránh lỗi khóa ngoại.
  - Risk Posture: `HIGH` (Tác động tới khả năng lưu trữ tập tin của các Tenant liên quan).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` xóa một `StorageProvider` không còn sử dụng. Đảm bảo an toàn dữ liệu bằng cách tự động gỡ liên kết (`StorageProviderId = null`) của toàn bộ các Tenant trước khi xóa thực thể Provider khỏi CSDL.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.Manage`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `DeleteStorageProviderCommand` với `Id` hợp lệ, `THEN` hệ thống `SHALL` tìm Provider, duyệt danh sách Tenant đang liên kết và đặt `tenant.StorageProviderId = null`, sau đó xóa bản ghi `StorageProvider` khỏi CSDL và trả về `true`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy `StorageProvider` theo `Id`, `THEN` hệ thống `SHALL` trả về `false`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-INTEGRITY-01:** Thao tác unbind Tenant và xóa Provider phải được thực thi trong cùng một transaction.

---

## 5. Data Model & Error Contract
- **Command:** `DeleteStorageProviderCommand(Id)`
- **Response:** `bool`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Xóa Storage Provider thành công
  Given StorageProvider "Local Disk Cu" đang được liên kết bởi 2 Tenant
  When SystemAdmin gửi DeleteStorageProviderCommand
  Then hệ thống gỡ StorageProviderId của 2 Tenant về null
  And xóa bản ghi StorageProvider khỏi CSDL
  And trả về true
```

---

## 7. Out of Scope
- Tự động di chuyển dữ liệu (data migration) của các tệp tin cũ sang một Provider mới (cần module Data Migration riêng).

---

## 8. Feature Lock
- **Locked Scope:** Unbind Tenants và xóa bản ghi StorageProvider.
- **Change Path:** `/sdd-update --feature=delete-storage-provider --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `delete-storage-provider` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature delete-storage-provider.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
