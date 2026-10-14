# SPEC: Update User (Cập nhật Thông tin Người dùng)

- **Feature Slug:** `update-user`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Cập nhật thông tin định danh cá nhân người dùng trong hệ thống.
  - Risk Posture: `MEDIUM` (Ảnh hưởng thông tin liên lạc và hồ sơ).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cho phép `SystemAdmin` cập nhật các thông tin cơ bản của tài khoản người dùng (Họ, Tên, Số điện thoại, Trạng thái kích hoạt).

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.Update`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi `UpdateUserCommand` với `Id` hợp lệ, `THEN` hệ thống `SHALL` cập nhật `FirstName`, `LastName`, `PhoneNumber`, `IsActive`, cập nhật `UpdatedAt = UtcNow`, lưu vào CSDL và trả về `UserDto`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy người dùng theo `Id` được cung cấp, `THEN` hệ thống `SHALL` trả về `Result<UserDto>.Failure("User with ID '{request.Id}' was not found.")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-AUDIT-01:** Phải ghi nhận thời điểm cập nhật `UpdatedAt`.

---

## 5. Data Model & Error Contract
- **Command:** `UpdateUserCommand(Id, FirstName, LastName, PhoneNumber, IsActive)`
- **Response:** `Result<UserDto>`
- **Errors:**
  - `UserNotFound`: `"User with ID '{id}' was not found."`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Cập nhật thông tin người dùng thành công
  Given người dùng có Id "user-123" tồn tại
  When SystemAdmin gửi lệnh cập nhật họ tên thành "Nguyen" "Van A"
  Then hệ thống cập nhật trường FirstName và LastName
  And trả về UserDto với FullName "Nguyen Van A"
```

---

## 7. Out of Scope
- Thay đổi mật khẩu người dùng (UC `reset-user-password`).
- Gán lại vai trò hoặc phòng ban (UC `assign-user-roles`).

---

## 8. Feature Lock
- **Locked Scope:** Chỉnh sửa thông tin hồ sơ User và IsActive.
- **Change Path:** `/sdd-update --feature=update-user --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `update-user` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature update-user.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
