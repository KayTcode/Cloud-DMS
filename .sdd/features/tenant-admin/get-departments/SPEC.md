# SPEC: Get Departments (Truy vấn Danh sách Phòng ban Tổ chức)

- **Feature Slug:** `get-departments`
- **Module:** `TenantAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vấn danh sách toàn bộ phòng ban của tổ chức kèm thông tin nhân sự, Trưởng phòng đại diện và số liệu thống kê hạn mức/dung lượng lưu trữ; tối ưu hóa hiệu năng đọc bằng `AsNoTracking()` và `AsSplitQuery()`.
  - Risk Posture: `MEDIUM` (Truy vấn dữ liệu, bảo mật Multi-Tenancy).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `TenantAdmin` truy vấn toàn bộ danh sách phòng ban thuộc tổ chức của mình. Kết quả cung cấp cái nhìn tổng quan về cơ cấu tổ chức bao gồm:
- Thông tin phòng ban: Mã phòng (`Code`), tên phòng (`Name`), mô tả (`Description`), trạng thái hoạt động (`IsActive`).
- Nhân sự: Số lượng thành viên (`HeadCount`), Trưởng phòng (`Lead`), Email Trưởng phòng (`AdminEmail`).
- Lưu trữ: Hạn mức đã cấp (`StorageQuotaBytes`, `AllocatedStorageGB`), dung lượng đã dùng thực tế (`StorageUsedBytes`, `UsedStorageGB`).

---

## 2. Actors & Roles

- **Actor:** `TenantAdmin` (Sở hữu quyền `department.read` trong phạm vi Tenant).
- **Unauthorized:** Mọi vai trò khác hoặc truy cập trái phép Tenant khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` TenantAdmin gửi `GetDepartmentsQuery(TenantId)`, `THEN` hệ thống `SHALL` truy vấn tất cả phòng ban thuộc `TenantId`, nạp quan hệ Users và Roles, sắp xếp theo tên phòng ban A-Z và trả về `Result<List<DepartmentDto>>`.
- **EARS-02 (State-driven):** `WHILE` truy vấn dữ liệu phòng ban, hệ thống `SHALL` thực thi với `AsNoTracking()` và `AsSplitQuery()` để tối ưu hóa bộ nhớ và tốc độ xử lý của EF Core.
- **EARS-03 (Ubiquitous):** Hệ thống `SHALL` tự động trích xuất người dùng có vai trò `DepartmentAdmin` trong phòng ban để hiển thị thông tin `Lead` và `AdminEmail`; nếu chưa có Trưởng phòng được bổ nhiệm, hệ thống `SHALL` hiển thị `"Chưa chỉ định"`.
- **EARS-04 (Ubiquitous):** Hệ thống `SHALL` tự động quy đổi `StorageQuotaBytes` và `StorageUsedBytes` sang đơn vị Gigabytes làm tròn 2 chữ số thập phân (`AllocatedStorageGB`, `UsedStorageGB`).
- **EARS-05 (Unwanted):** `IF` người dùng yêu cầu truy vấn với `TenantId` không thuộc phạm vi tài khoản của mình, `THEN` hệ thống `SHALL` từ chối thực thi và trả về lỗi truy cập trái phép.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-PERF-01:** Thời gian phản hồi truy vấn phải **< 200ms** cho tổ chức có tối đa 100 phòng ban và 5,000 nhân sự.
- **NFR-SEC-01 (Multi-Tenancy):** Truy vấn luôn lọc chặt chẽ `d.TenantId == request.TenantId`, không để lọt bất kỳ phòng ban nào của tổ chức khác.

---

## 5. Data Model & Error Contract

- **Query:** `GetDepartmentsQuery(Guid TenantId) : IRequest<Result<List<DepartmentDto>>>`
- **Response:** `Result<List<DepartmentDto>>`
- **HTTP Endpoint:** `GET /api/tenant/{tenantId}/departments` (Policy: `department.read`)

---

## 6. Acceptance Criteria (BDD)

```gherkin
Scenario: Lấy danh sách phòng ban của Tenant thành công
  Given Tenant "Acme Corp" có 3 phòng ban: "Kỹ thuật", "Kinh doanh", "Nhân sự"
  And phòng ban "Kỹ thuật" có 10 nhân sự và Trưởng phòng "lead_kt@acme.com"
  When TenantAdmin gửi yêu cầu lấy danh sách phòng ban
  Then hệ thống trả về danh sách gồm 3 phần tử
  And danh sách được sắp xếp theo thứ tự bảng chữ cái
  And thông tin phòng ban "Kỹ thuật" có HeadCount = 10, AdminEmail = "lead_kt@acme.com"
  And dung lượng được quy đổi sang GB chính xác
```

---

## 7. Out of Scope

- Phân trang danh sách phòng ban (số lượng phòng ban trong một tổ chức thường < 100 nên tải toàn bộ danh sách).

---

## 8. Feature Lock

- **Locked Scope:** Truy vấn và ánh xạ DTO trong `GetDepartmentsQueryHandler`.
- **Change Path:** `/sdd-update --feature=get-departments --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-departments` only.

---

## 9. AI Recommendation và Human Final Review

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-departments.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
