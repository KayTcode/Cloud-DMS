# SPEC: Get Users With Pagination (Lấy Danh sách Người dùng Phân trang)

- **Feature Slug:** `get-users-with-pagination`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vấn danh sách người dùng toàn hệ thống kèm bộ lọc đa tiêu chí và phân trang.
  - Risk Posture: `LOW` (Read-only query, tối ưu hiệu năng bộ nhớ).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cung cấp khả năng tra cứu danh sách người dùng toàn hệ thống cho `SystemAdmin` theo nhiều tiêu chí lọc (SearchTerm, Role, Tenant, Department, Trạng thái kích hoạt) và phân trang.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.View`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi truy vấn `GetUsersWithPaginationQuery` kèm các tham số lọc, `THEN` hệ thống `SHALL` áp dụng bộ lọc tương ứng, thực hiện phân trang và trả về `Result<PaginatedList<UserDto>>`.
- **EARS-02 (State-driven):** `WHILE` xử lý truy vấn phân trang, hệ thống `SHALL` dùng cơ chế `AsNoTracking()` và eager-loading các liên kết `Tenant`, `Department`, `UserRoles` để tối ưu bộ nhớ.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Thời gian phản hồi truy vấn phân trang phải đạt dưới **300ms** cho tập dữ liệu lên đến 50,000 người dùng.

---

## 5. Data Model & Error Contract
- **Query:** `GetUsersWithPaginationQuery(SearchTerm, RoleId, TenantId, DepartmentId, IsActive, PageNumber = 1, PageSize = 10)`
- **Response:** `Result<PaginatedList<UserDto>>`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Tra cứu danh sách người dùng có phân trang
  Given có 25 người dùng trong hệ thống
  When SystemAdmin yêu cầu PageNumber = 1, PageSize = 10
  Then hệ thống trả về 10 bản ghi đầu tiên
  And TotalPages = 3, TotalCount = 25
```

---

## 7. Out of Scope
- Xuất danh sách người dùng ra định dạng Excel/CSV.

---

## 8. Feature Lock
- **Locked Scope:** Truy vấn phân trang và lọc người dùng toàn hệ thống.
- **Change Path:** `/sdd-update --feature=get-users-with-pagination --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-users-with-pagination` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-users-with-pagination.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
