# SPEC: Test Storage Provider Connection (Kiểm thử Kết nối Storage Provider)

- **Feature Slug:** `test-storage-provider-connection`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Kiểm thử kết nối động với các bên thứ 3 (Google Drive, Microsoft OneDrive, Local Disk) trước khi lưu cấu hình.
  - Risk Posture: `MEDIUM` (Giao tiếp mạng bên thứ ba, timeout và độ trễ).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cho phép `SystemAdmin` kiểm tra tính hợp lệ của thông tin cấu hình Cloud Storage (Client ID, Secret, Refresh Token, Service Account Key) mà không cần ghi trực tiếp vào cơ sở dữ liệu.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.Manage`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `TestStorageProviderConnectionCommand` với cấu hình hợp lệ, `THEN` hệ thống `SHALL` tạo adapter tạm thời thông qua `IStorageServiceFactory`, gọi `GetProviderQuotaAsync`, đo độ trễ và trả về `TestConnectionResult` với `Success = true`, các thông số dung lượng và độ trễ (`LatencyMs`).
- **EARS-02 (Unwanted):** `IF` kết nối thất bại (sai thông tin xác thực, hết hạn token, lỗi mạng), `THEN` hệ thống `SHALL` bắt ngoại lệ và trả về `TestConnectionResult` với `Success = false` và nội dung thông báo lỗi cụ thể mà không làm gián đoạn hệ thống.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Thời gian Timeout cho yêu cầu kiểm thử mạng bên thứ 3 tối đa là **15 giây**.

---

## 5. Data Model & Error Contract
- **Command:** `TestStorageProviderConnectionCommand(Type, ConfigJson)`
- **Response:** `TestConnectionResult(Success, Message, TotalCapacityBytes, UsedCapacityBytes, AvailableCapacityBytes, LatencyMs)`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Kiểm thử kết nối Google Drive thành công
  Given SystemAdmin nhập cấu hình Service Account hợp lệ của Google Drive
  When hệ thống thực thi TestStorageProviderConnectionCommand
  Then kết quả trả về Success = true
  And hiển thị TotalCapacityBytes và UsedCapacityBytes thực tế của Drive
```

---

## 7. Out of Scope
- Lưu thông tin cấu hình vào CSDL (xử lý tại UC `connect-storage-provider`).

---

## 8. Feature Lock
- **Locked Scope:** Kiểm tra kết nối và đo quota thực tế từ raw JSON config.
- **Change Path:** `/sdd-update --feature=test-storage-provider-connection --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `test-storage-provider-connection` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature test-storage-provider-connection.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
