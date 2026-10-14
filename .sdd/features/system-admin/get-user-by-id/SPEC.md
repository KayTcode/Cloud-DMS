# SPEC: Get User By Id (Lấy Chi tiết Người dùng)

- **Feature Slug:** `get-user-by-id`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vấn hồ sơ chi tiết của người dùng kèm đầy đủ các mối quan hệ đa tầng (Tenant, Department, Roles, Permissions).
  - Risk Posture: `LOW` (Read-only single entity query).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cho phép `SystemAdmin` xem đầy đủ thông tin chi tiết của một tài khoản người dùng, bao gồm thông tin cá nhân, tổ chức trực thuộc và danh sách các quyền hạn (`Permissions`) được phân bổ.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.View`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi truy vấn `GetUserByIdQuery` với `Id` hợp lệ, `THEN` hệ thống `SHALL` nạp thông tin người dùng cùng các bảng liên kết (`Tenant`, `Department`, `UserRoles`, `RolePermissions`, `Permission`) bằng cơ chế `AsNoTracking()` và trả về `Result<UserDto>.Success(userDto)`.
- **EARS-02 (Unwanted):** `IF` không tìm thấy người dùng theo `Id`, `THEN` hệ thống `SHALL` trả về `Result<UserDto>.Failure("User with ID '{request.Id}' was not found.")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Truy vấn đơn lẻ phải thực hiện trong thời gian dưới **50ms**.

---

## 5. Data Model & Error Contract
- **Query:** `GetUserByIdQuery(Id)`
- **Response:** `Result<UserDto>`
- **Errors:**
  - `UserNotFound`: `"User with ID '{id}' was not found."`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Lấy thông tin chi tiết người dùng thành công
  Given người dùng có Id "user-123" tồn tại với vai trò "TenantAdmin"
  When SystemAdmin gửi GetUserByIdQuery với Id "user-123"
  Then hệ thống trả về UserDto chứa đầy đủ tên, email, danh sách Roles và danh sách Permissions
```

---

## 7. Out of Scope
- Lấy thông tin lịch sử hoạt động của người dùng (xử lý tại UC `get-audit-logs`).

---

## 8. Feature Lock
- **Locked Scope:** Truy vấn chi tiết thông tin và quyền của User theo Id.
- **Change Path:** `/sdd-update --feature=get-user-by-id --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-user-by-id` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-user-by-id.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
