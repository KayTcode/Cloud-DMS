# SPEC: Change User Status (Thay đổi Trạng thái Người dùng)

- **Feature Slug:** `change-user-status`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Quản lý kích hoạt/khóa tài khoản, bảo vệ SystemAdmin duy nhất, thu hồi Refresh Tokens.
  - Risk Posture: `HIGH` (Nguy cơ khóa nhầm quản trị viên cấp cao nhất).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` kích hoạt (`Active`) hoặc tạm khóa/vô hiệu hóa (`Inactive`) tài khoản. Đảm bảo quy tắc: **Không bao giờ được phép vô hiệu hóa tài khoản SystemAdmin hoạt động duy nhất của hệ thống**, và khi vô hiệu hóa phải lập tức thu hồi mọi `RefreshTokens`.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.ChangeStatus`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `ChangeUserStatusCommand` với `IsActive = true`, `THEN` hệ thống `SHALL` kích hoạt tài khoản (`user.Active()`), tăng `AuthorizationVersion` thêm 1, lưu CSDL và trả về `Result.Success()`.
- **EARS-02 (Event-driven):** `WHEN` SystemAdmin gửi lệnh với `IsActive = false` cho một tài khoản thông thường, `THEN` hệ thống `SHALL` vô hiệu hóa tài khoản (`user.Inactive()`), thu hồi toàn bộ `RefreshTokens` còn hiệu lực của tài khoản đó với lý do `user_deactivated`, tăng `AuthorizationVersion` thêm 1 và trả về `Result.Success()`.
- **EARS-03 (Unwanted):** `IF` tài khoản bị yêu cầu vô hiệu hóa (`IsActive = false`) mang vai trò `SystemAdmin` và số lượng `SystemAdmin` đang hoạt động trong toàn hệ thống `<= 1`, `THEN` hệ thống `SHALL` từ chối thao tác và trả về `Result.Failure("Cannot deactivate the only active System Administrator in the system.")`.
- **EARS-04 (Unwanted):** `IF` không tìm thấy người dùng theo `Id`, `THEN` hệ thống `SHALL` trả về `Result.Failure("User with ID '{request.Id}' was not found.")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Phải lập tức thu hồi tất cả `RefreshTokens` trong cùng một transaction khi tài khoản bị khóa.
- **NFR-SEC-02:** Tăng `AuthorizationVersion` để làm mất hiệu lực token ở chu kỳ request tiếp theo.

---

## 5. Data Model & Error Contract
- **Command:** `ChangeUserStatusCommand(Id, IsActive)`
- **Response:** `Result`
- **Errors:**
  - `OnlyAdminProtected`: `"Cannot deactivate the only active System Administrator in the system."`
  - `UserNotFound`: `"User with ID '{id}' was not found."`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Chặn vô hiệu hóa tài khoản SystemAdmin duy nhất
  Given hệ thống chỉ có duy nhất 1 tài khoản có vai trò "SystemAdmin" đang hoạt động
  When SystemAdmin yêu cầu vô hiệu hóa (IsActive = false) tài khoản này
  Then hệ thống từ chối thực hiện
  And trả về lỗi "Cannot deactivate the only active System Administrator in the system."
  And tài khoản vẫn duy trì IsActive = true
```

---

## 7. Out of Scope
- Gửi thông báo email tự động về việc tài khoản bị khóa.

---

## 8. Feature Lock
- **Locked Scope:** Chuyển đổi trạng thái Active/Inactive, bảo vệ SystemAdmin duy nhất, thu hồi Refresh Tokens.
- **Change Path:** `/sdd-update --feature=change-user-status --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `change-user-status` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature change-user-status.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
