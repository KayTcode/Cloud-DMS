# SPEC: Get Audit Logs (Truy vấn Nhật ký Kiểm toán Toàn Hệ thống)

- **Feature Slug:** `get-audit-logs`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vết hành động người dùng xuyên suốt toàn bộ các Tenant, phục vụ giám sát an ninh và tuân thủ.
  - Risk Posture: `MEDIUM` (Truy vấn dữ liệu lớn và nhạy cảm về bảo mật).
  - Review Route: `Standard Feature Review`

---

## 1. Context & Goal
Cung cấp cho `SystemAdmin` khả năng kiểm tra, truy vết tất cả các thao tác nghiệp vụ diễn ra trên toàn hệ thống DMS. Hỗ trợ lọc theo Tenant, Hành động (`Action`), Từ khóa tìm kiếm tự do (`SearchTerm`) và tự động suy luận trạng thái kết quả (`SUCCESS`, `FAILED`, `WARNING`).

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.AuditLogs.View`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi truy vấn `GetAuditLogsQuery` kèm các tham số lọc, `THEN` hệ thống `SHALL` áp dụng bộ lọc trên bảng `AuditLogs`, sắp xếp giảm dần theo thời gian tạo (`CreatedAt`), phân trang và trả về `Result<PaginatedList<AuditLogDto>>`.
- **EARS-02 (State-driven):** `WHILE` xử lý kết quả, hệ thống `SHALL` tự động phân loại trạng thái:
  - Nếu Action hoặc Details chứa từ khóa lỗi ("Failed", "thất bại", "fail") → `status = "FAILED"`.
  - Nếu Action hoặc Details chứa cảnh báo ("Warning", "cảnh báo", "warn") → `status = "WARNING"`.
  - Còn lại → `status = "SUCCESS"`.
- **EARS-03 (State-driven):** `WHILE` truy vấn bảng dữ liệu lớn, hệ thống `SHALL` sử dụng `AsNoTracking()` và `AsSplitQuery()` để tối ưu hóa tài nguyên kết nối CSDL.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Thời gian truy vấn phân trang phải hoàn thành trong **< 300ms** đối với bảng log có trên 100,000 dòng.
- **NFR-SEC-01:** Che giấu các dữ liệu nhạy cảm hoặc token bí mật nếu có trong trường `Details`.

---

## 5. Data Model & Error Contract
- **Query:** `GetAuditLogsQuery(PageNumber = 1, PageSize = 20, SearchTerm, Action, TenantId)`
- **Response:** `Result<PaginatedList<AuditLogDto>>`

---

## 6. Acceptance Criteria (BDD)
```gherkin
Scenario: Tra cứu nhật ký kiểm toán với từ khóa
  Given hệ thống có các bản ghi log đăng nhập và thay đổi quyền
  When SystemAdmin tìm kiếm với SearchTerm "Failed"
  Then hệ thống trả về danh sách các bản ghi có Status là "FAILED"
  And danh sách được sắp xếp từ mới nhất đến cũ nhất
```

---

## 7. Out of Scope
- Tự động xóa hoặc archive log định kỳ sang cold storage (xử lý ở background worker).

---

## 8. Feature Lock
- **Locked Scope:** Truy vấn phân trang, tìm kiếm và phân loại trạng thái Audit Logs toàn hệ thống.
- **Change Path:** `/sdd-update --feature=get-audit-logs --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `get-audit-logs` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature get-audit-logs.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
