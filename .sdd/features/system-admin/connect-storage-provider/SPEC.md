# SPEC: Connect Storage Provider (Kết nối và Lưu Storage Provider)

- **Feature Slug:** `connect-storage-provider`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Lưu cấu hình nhà cung cấp lưu trữ đám mây, bắt buộc mã hóa AES-256 đối xứng và khởi tạo thư mục gốc.
  - Risk Posture: `HIGH` (Bảo mật thông tin đăng nhập đám mây, lưu trữ tập tin hệ thống).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` thêm mới một `StorageProvider` vào hệ thống. Hệ thống sẽ tự động xác minh quota thực tế, tạo thư mục gốc `CloudDMS_Root` trên đám mây, và mã hóa bí mật cấu hình bằng thuật toán AES-256 trước khi lưu CSDL.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.Manage`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `ConnectStorageProviderCommand` với thông tin hợp lệ, `THEN` hệ thống `SHALL` tạo adapter kiểm tra quota thực tế, tự động tạo thư mục gốc `CloudDMS_Root` trên đám mây, mã hóa chuỗi `ConfigJson` bằng `IAesEncryptionService`, lưu vào bảng `StorageProviders` và trả về `StorageProviderDto`.
- **EARS-02 (Optional):** `WHERE` việc tạo thư mục trên đám mây bị từ chối quyền hoặc thư mục đã tồn tại, hệ thống `SHALL` fallback sử dụng `CloudDMS_Root` làm root folder id mặc định.
- **EARS-03 (Unwanted):** `IF` thông tin cấu hình không thể kết nối hoặc lỗi xác thực từ phía nhà cung cấp, `THEN` hệ thống `SHALL` ném lỗi và không lưu bản ghi vào CSDL.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01 (Zero Secret Policy):** Tuyệt đối không lưu cấu hình ở dạng văn bản thuần (plaintext); bắt buộc mã hóa AES-256 đối xứng. Không xuất trường cấu hình giải mã ra DTO hoặc API response.

---

## 5. Data Model & Error Contract
- **Command:** `ConnectStorageProviderCommand(Name, Type, ConfigJson)`
- **Response:** `StorageProviderDto`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Kết nối Cloud Storage Provider thành công
  Given SystemAdmin nhập cấu hình hợp lệ của Local Disk
  When hệ thống xử lý ConnectStorageProviderCommand
  Then tạo bản ghi StorageProvider mới trong CSDL
  And chuỗi ConfigEncrypted được lưu dưới dạng chuỗi mã hóa
  And thư mục gốc RootFolderId được thiết lập
```

---

## 7. Out of Scope
- Tự động đồng bộ các tệp tin cũ trên Cloud vào DMS (chỉ quản lý các tệp mới tải lên).

---

## 8. Feature Lock
- **Locked Scope:** Kiểm tra dung lượng, tạo folder gốc, mã hóa AES-256 và lưu StorageProvider.
- **Change Path:** `/sdd-update --feature=connect-storage-provider --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `connect-storage-provider` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature connect-storage-provider.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
