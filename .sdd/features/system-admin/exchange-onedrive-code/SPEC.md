# SPEC: Exchange OneDrive Code (Trao đổi Mã Ủy quyền OneDrive OAuth2)

- **Feature Slug:** `exchange-onedrive-code`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Tích hợp xác thực OAuth2 với Microsoft Graph, xử lý token offline access và bảo vệ credential.
  - Risk Posture: `HIGH` (Ủy quyền OAuth2 bên thứ ba và quản lý khóa bí mật).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` hoàn tất luồng OAuth2 Authorization Code Flow với Microsoft OneDrive. Hệ thống sẽ trao đổi mã Code với Microsoft Identity Platform để nhận `access_token` và `refresh_token`, sau đó đóng gói cấu hình, mã hóa AES-256 và lưu hoặc cập nhật Provider "Microsoft OneDrive".

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Storage.Manage`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi `ExchangeOneDriveCodeCommand` kèm mã Code hợp lệ từ Microsoft, `THEN` hệ thống `SHALL` gửi POST request tới Microsoft Identity Token Endpoint, trích xuất `refresh_token`, tạo file cấu hình JSON, kiểm tra hạn mức qua adapter, mã hóa AES-256 và lưu vào bảng `StorageProviders` (upsert bản ghi "Microsoft OneDrive") rồi trả về kết quả thành công.
- **EARS-02 (Unwanted):** `IF` Microsoft Identity Platform trả về lỗi (mã Code hết hạn, Client Secret không khớp), `THEN` hệ thống `SHALL` trả về `ExchangeOneDriveCodeResult(Success = false, Message = "Lỗi xác thực mã với Microsoft...")`.
- **EARS-03 (Unwanted):** `IF` phản hồi thành công từ Microsoft nhưng không chứa `refresh_token` (do thiếu scope `offline_access`), `THEN` hệ thống `SHALL` hủy thao tác và thông báo yêu cầu cấp quyền `offline_access`.
- **EARS-04 (Ubiquitous):** Hệ thống `SHALL` không bao giờ trả về chuỗi Token bí mật hoặc JSON cấu hình giải mã về phía trình duyệt trong đối tượng kết quả.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Bảo vệ Zero Secret Policy: Tất cả token và secret của Microsoft phải được mã hóa trước khi ghi đĩa hoặc CSDL.

---

## 5. Data Model & Error Contract
- **Command:** `ExchangeOneDriveCodeCommand(ClientId, ClientSecret, Code, RedirectUri)`
- **Response:** `ExchangeOneDriveCodeResult(Success, Message, TotalCapacityBytes, UsedCapacityBytes, AvailableCapacityBytes, LatencyMs)`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Trao đổi mã OAuth2 Microsoft OneDrive thành công
  Given SystemAdmin nhận được mã Code hợp lệ từ Microsoft sau khi cấp quyền offline_access
  When hệ thống gọi ExchangeOneDriveCodeCommand
  Then hệ thống đổi thành công refresh_token từ Microsoft
  And cập nhật StorageProvider "Microsoft OneDrive" với cấu hình mã hóa AES-256
  And trả về Success = true
```

---

## 7. Out of Scope
- Tạo ứng dụng Microsoft Entra ID trên cổng Azure (do Admin thực hiện thủ công ngoài nền tảng).

---

## 8. Feature Lock
- **Locked Scope:** Giao tiếp OAuth2 token endpoint của Microsoft, đóng gói cấu hình và mã hóa CSDL.
- **Change Path:** `/sdd-update --feature=exchange-onedrive-code --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `exchange-onedrive-code` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature exchange-onedrive-code.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
