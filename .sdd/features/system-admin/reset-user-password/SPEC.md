# SPEC: Reset User Password (Đặt lại Mật khẩu Người dùng)

- **Feature Slug:** `reset-user-password`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Quản trị viên cấp cao can thiệp đặt lại mật khẩu cho tài khoản người dùng và thu hồi phiên làm việc cũ.
  - Risk Posture: `HIGH` (Bảo mật tài khoản và xác thực).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` đặt lại mật khẩu mới cho bất kỳ người dùng nào khi được yêu cầu, đồng thời bắt buộc phải **thu hồi toàn bộ Refresh Tokens** còn hiệu lực của tài khoản đó để ngăn chặn việc phiên cũ tiếp tục duy trì đăng nhập.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.ResetPassword`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `ResetUserPasswordCommand` với mật khẩu mới thỏa mãn độ phức tạp, `THEN` hệ thống `SHALL` băm mật khẩu mới bằng `IPasswordHasher`, lưu vào `PasswordHash`, cập nhật `UpdatedAt = UtcNow`, thu hồi (`Revoke`) tất cả `RefreshTokens` chưa bị thu hồi của người dùng với lý do `password_reset` và trả về `Result.Success()`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy người dùng theo `Id`, `THEN` hệ thống `SHALL` trả về `Result.Failure("User with ID '{request.Id}' was not found.")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Mật khẩu mới bắt buộc phải băm bằng thuật toán an toàn BCrypt trước khi ghi CSDL.
- **NFR-SEC-02:** Thu hồi ngay lập tức toàn bộ RefreshToken còn hạn của tài khoản.

---

## 5. Data Model & Error Contract
- **Command:** `ResetUserPasswordCommand(Id, NewPassword)`
- **Response:** `Result`
- **Errors:**
  - `UserNotFound`: `"User with ID '{id}' was not found."`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Đặt lại mật khẩu thành công và hủy các phiên cũ
  Given người dùng "user-123" có 2 RefreshToken đang hoạt động
  When SystemAdmin gửi lệnh đặt lại mật khẩu mới
  Then mật khẩu mới được băm và lưu vào PasswordHash
  And cả 2 RefreshToken đều chuyển sang trạng thái RevokedAt != null với lý do "password_reset"
```

---

## 7. Out of Scope
- Quy trình tự reset mật khẩu qua link quên mật khẩu gửi về email của người dùng cuối.

---

## 8. Feature Lock
- **Locked Scope:** Băm mật khẩu mới, cập nhật CSDL và thu hồi Refresh Tokens.
- **Change Path:** `/sdd-update --feature=reset-user-password --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `reset-user-password` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature reset-user-password.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
