# SPEC: Change User Password & Security (Đổi Mật khẩu & Bảo mật Tài khoản)

- **Feature Slug:** `change-user-password`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Nghiệp vụ quản lý an toàn xác thực tài khoản, yêu cầu băm BCrypt, xác thực mật khẩu hiện tại và thu hồi token cũ.
  - Risk Posture: `HIGH`
  - Review Route: `Security Review Mandatory`

---

## 1. Context & Goal
Cho phép người dùng (`Employee` / `User`) trong tab "Bảo mật" của trang Hồ sơ ([ProfileView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/ProfileView.tsx)) thay đổi mật khẩu đăng nhập của mình, bảo đảm kiểm tra đúng mật khẩu hiện tại, kiểm tra độ mạnh của mật khẩu mới và xác nhận mật khẩu trùng khớp.

---

## 2. Actors & Roles
- **Actor:** Mọi người dùng đã đăng nhập tự đổi mật khẩu cho chính mình.
- **Unauthorized:** Người dùng chưa đăng nhập bị từ chối (`401 Unauthorized`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng nhập Mật khẩu hiện tại, Mật khẩu mới và Xác nhận mật khẩu mới rồi nhấn "Đổi mật khẩu", `THEN` Frontend `SHALL` gửi request `POST /api/auth/change-password` với payload `{ currentPassword, newPassword }`.
- **EARS-02 (Event-driven):** `WHEN` Backend nhận request, hệ thống `SHALL` xác thực mật khẩu cũ bằng BCrypt; nếu hợp lệ, băm mật khẩu mới bằng BCrypt, cập nhật vào CSDL, tăng `AuthorizationVersion` để vô hiệu hóa các Access Token cũ và trả về `Result<bool>.Success(true)`.
- **EARS-03 (Event-driven):** `WHEN` đổi mật khẩu thành công, Frontend `SHALL` xóa trắng các ô nhập liệu và hiển thị thông báo toast: `"Đổi mật khẩu tài khoản thành công!"`
- **EARS-04 (Unwanted):** `IF` Mật khẩu mới không khớp với Xác nhận mật khẩu, `THEN` Frontend `SHALL` chặn gửi request và báo lỗi: `"Mật khẩu mới không trùng khớp."`
- **EARS-05 (Unwanted):** `IF` Mật khẩu mới dưới 8 ký tự hoặc thiếu chữ hoa/chữ số, `THEN` hệ thống `SHALL` từ chối với lỗi: `"Mật khẩu mới phải có ít nhất 8 ký tự, bao gồm chữ hoa và chữ số."`
- **EARS-06 (Unwanted):** `IF` Mật khẩu hiện tại nhập vào không chính xác, `THEN` Backend `SHALL` từ chối với lỗi HTTP 400: `"Mật khẩu hiện tại không chính xác."`

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Mật khẩu phải được băm an toàn bằng BCrypt với work factor tối thiểu 11.
- **NFR-SEC-02:** Tự động tăng `AuthorizationVersion` để làm mất hiệu lực toàn bộ JWT token cũ trên các thiết bị khác sau khi đổi mật khẩu.

---

## 5. Data Model & Error Contract
- **Endpoint:** `POST /api/auth/change-password`
- **Payload:** `{ currentPassword: string, newPassword: string }`
- **Response:** `Result<bool>`
- **Errors:** `InvalidCurrentPassword` (400), `WeakPassword` (400), `PasswordMismatch` (400).

---

## 6. Acceptance Criteria (BDD)
### Kịch bản 1: Đổi mật khẩu thành công
- **Given** Người dùng nhập đúng mật khẩu hiện tại và mật khẩu mới đủ độ mạnh `"MatKhauMoi@2026"`.
- **When** Người dùng nhấn "Đổi mật khẩu".
- **Then** Hệ thống báo thành công, mật khẩu mới được cập nhật và các trường form được làm sạch.

### Kịch bản 2: Nhập sai mật khẩu hiện tại
- **Given** Người dùng nhập sai mật khẩu hiện tại.
- **When** Người dùng nhấn "Đổi mật khẩu".
- **Then** Hệ thống báo lỗi `"Mật khẩu hiện tại không chính xác"` và không thay đổi mật khẩu.

---

## 7. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature change-user-password.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
