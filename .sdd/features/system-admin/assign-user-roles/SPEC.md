# SPEC: Assign User Roles (Gán Vai trò và Đơn vị cho Người dùng)

- **Feature Slug:** `assign-user-roles`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Quản trị phân quyền hệ thống, điều phối người dùng giữa các Tenant và Department.
  - Risk Posture: `HIGH` (Đổi đặc quyền truy cập và ranh giới bảo mật Tenant).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` cập nhật vai trò (`Roles`), chuyển đổi hoặc gán người dùng vào `Tenant` và `Department` tương ứng, đảm bảo tính nhất quán về cấu trúc tổ chức và làm mất hiệu lực các quyền cũ trên JWT.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.AssignRoles`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `AssignUserRolesCommand` với danh sách `RoleIds`, `TenantId`, `DepartmentId` hợp lệ, `THEN` hệ thống `SHALL` cập nhật lại liên kết `UserRoles`, gán `TenantId` và `DepartmentId`, tăng `AuthorizationVersion` thêm 1 (nếu có thay đổi quyền) và trả về `UserDto`.
- **EARS-02 (Unwanted):** `IF` `DepartmentId` được cung cấp nhưng không có `TenantId`, `THEN` hệ thống `SHALL` từ chối và trả về `Result<UserDto>.Failure("A tenant must be selected when assigning a department.")`.
- **EARS-03 (Unwanted):** `IF` `DepartmentId` không thuộc về `TenantId` đã chọn, `THEN` hệ thống `SHALL` từ chối và trả về `Result<UserDto>.Failure("Department '{name}' does not belong to Tenant '{tenantName}'.")`.
- **EARS-04 (Unwanted):** `IF` một hoặc nhiều `RoleId` trong danh sách không tồn tại trong CSDL, `THEN` hệ thống `SHALL` trả về `Result<UserDto>.Failure("One or more role IDs are invalid.")`.
- **EARS-05 (Unwanted):** `IF` không tìm thấy người dùng theo `Id`, `THEN` hệ thống `SHALL` trả về `Result<UserDto>.Failure("User with ID '{request.Id}' was not found.")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Phải tăng `AuthorizationVersion` khi có sự thay đổi về vai trò/tổ chức để ép các token cũ phải refresh lại quyền mới.

---

## 5. Data Model & Error Contract
- **Command:** `AssignUserRolesCommand(Id, RoleIds, TenantId, DepartmentId)`
- **Response:** `Result<UserDto>`
- **Errors:**
  - `DepartmentWithoutTenant`: `"A tenant must be selected when assigning a department."`
  - `DepartmentMismatch`: `"Department '{name}' does not belong to Tenant '{tenantName}'."`
  - `InvalidRoles`: `"One or more role IDs are invalid."`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Gán vai trò và phòng ban thành công
  Given người dùng "user-123" thuộc Tenant "Tenant-A"
  When SystemAdmin gán vai trò "DepartmentAdmin" và Department "Kế Toán" thuộc "Tenant-A"
  Then hệ thống cập nhật UserRoles và DepartmentId
  And tăng AuthorizationVersion của người dùng
```

---

## 7. Out of Scope
- Tạo mới Role hoặc tùy biến Permission riêng lẻ (nằm ở use case quản lý Role).

---

## 8. Feature Lock
- **Locked Scope:** Đồng bộ Role, Tenant, Department cho User và quản lý AuthorizationVersion.
- **Change Path:** `/sdd-update --feature=assign-user-roles --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `assign-user-roles` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature assign-user-roles.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
