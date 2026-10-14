# SPEC: Allocate Department Storage Quota (Cấp phát & Điều chỉnh Hạn mức Lưu trữ Phòng ban)

- **Feature Slug:** `allocate-department-storage-quota`
- **Module:** `TenantAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Cấp phát và điều phối hạn mức dung lượng lưu trữ nội bộ giữa các phòng ban trong một Tenant; yêu cầu bảo đảm toàn vẹn dữ liệu giao dịch (`Serializable`) và áp dụng cơ chế kiểm tra 3 lớp an toàn để chống mất mát dữ liệu hoặc làm hỏng dữ liệu người dùng.
  - Risk Posture: `HIGH` (Toàn vẹn hạn mức lưu trữ phòng ban, chống Race Condition khi cấp phát đồng thời).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `TenantAdmin` điều chỉnh, tăng thêm hoặc thu hồi hạn mức lưu trữ (`StorageQuotaBytes`) của một phòng ban cụ thể trực thuộc tổ chức của mình. Tính năng bảo đảm 3 lớp kiểm tra an toàn nghiêm ngặt:
1. Không được hạ hạn mức thấp hơn dung lượng thực tế phòng ban đã sử dụng (`department.StorageUsedBytes`).
2. Không được hạ hạn mức thấp hơn tổng hạn mức mà Trưởng phòng đã phân bổ cho các thành viên trong phòng (`totalAllocatedToUsers`).
3. Tổng hạn mức cấp phát cho các phòng ban không được vượt quá hạn mức khả dụng của Tenant (`availableInTenant`).

---

## 2. Actors & Roles

- **Actor:** `TenantAdmin` (Sở hữu quyền `storage.manage` trong phạm vi Tenant).
- **Unauthorized:** Mọi vai trò khác hoặc TenantAdmin của tổ chức khác bị từ chối truy cập (`403 Forbidden` / `400 Bad Request`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` TenantAdmin gửi lệnh `AllocateDepartmentStorageQuotaCommand(TenantId, DepartmentId, QuotaBytes)` với hạn mức hợp lệ, `THEN` hệ thống `SHALL` mở transaction CSDL với mức cô lập `IsolationLevel.Serializable`, cập nhật `department.StorageQuotaBytes`, ghi `AuditLog` và trả về `DepartmentStorageQuotaDto`.
- **EARS-02 (Unwanted):** `IF` `QuotaBytes < 0`, `THEN` hệ thống `SHALL` từ chối thao tác và trả về thông báo lỗi: `"Hạn mức dung lượng không được là số âm."` (`Result.Failure`).
- **EARS-03 (Unwanted):** `IF` `TenantId` không tồn tại trong hệ thống, `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không tìm thấy thông tin Tổ chức (Tenant)."`
- **EARS-04 (Unwanted):** `IF` `DepartmentId` không tồn tại hoặc không thuộc về Tenant (`d.TenantId != request.TenantId`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không tìm thấy Phòng ban tương ứng trong tổ chức."`
- **EARS-05 (Unwanted):** `IF` `QuotaBytes` nhỏ hơn dung lượng phòng ban đã sử dụng (`department.StorageUsedBytes`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không thể hạ hạn mức phòng ban xuống thấp hơn dung lượng phòng ban đã sử dụng ({FormatBytes(department.StorageUsedBytes)})."` (Lớp bảo vệ 1).
- **EARS-06 (Unwanted):** `IF` `QuotaBytes` nhỏ hơn tổng hạn mức đã cấp cho các thành viên trong phòng (`totalAllocatedToUsers`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không thể hạ hạn mức phòng ban xuống thấp hơn tổng hạn mức đã cấp cho các thành viên trong phòng ({FormatBytes(totalAllocatedToUsers)}). Hãy thu hồi bớt hạn mức của thành viên trước."` (Lớp bảo vệ 2).
- **EARS-07 (Unwanted):** `IF` `QuotaBytes` vượt quá dung lượng khả dụng của Tenant (`availableInTenant = tenant.StorageQuotaBytes - otherDepartmentsQuota`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Vượt quá hạn mức lưu trữ khả dụng của Tổ chức! Tổ chức chỉ còn trống {FormatBytes(availableInTenant)} để cấp thêm cho phòng ban này."` (Lớp bảo vệ 3).
- **EARS-08 (Ubiquitous):** Hệ thống `SHALL` tự động ghi nhật ký kiểm toán `AuditLog` với `Action = "AllocateDepartmentQuota"`, lưu vết hạn mức cũ và hạn mức mới.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-RELIABILITY-01:** Bảo đảm tính nhất quán giao dịch và chống Race Condition: Giao dịch cấp phát quota phòng ban phải được thực thi ở mức cô lập `IsolationLevel.Serializable`.
- **NFR-RELIABILITY-02:** Bảo toàn dữ liệu người dùng: Không bao giờ cho phép hạ quota phòng ban dẫn đến tình trạng tổng quota thành viên vượt quá quota phòng ban.
- **NFR-SEC-01 (Multi-Tenancy):** Chỉ cho phép điều chỉnh phòng ban thuộc đúng Tenant của TenantAdmin đang đăng nhập (chống IDOR).

---

## 5. Data Model & Error Contract

- **Command:** `AllocateDepartmentStorageQuotaCommand(Guid TenantId, Guid DepartmentId, long QuotaBytes)`
- **Response:** `Result<DepartmentStorageQuotaDto>`
- **HTTP Endpoint:** `PUT /api/tenant/{tenantId}/departments/{departmentId}/quota` (Policy: `storage.manage`)
- **Exceptions / Failures:**
  - Hạn mức âm: `400 Bad Request`
  - Tenant / Department không tìm thấy: `400 Bad Request`
  - Vi phạm 3 lớp an toàn hạn mức: `400 Bad Request` kèm thông báo chi tiết định dạng dung lượng.

---

## 6. Acceptance Criteria (BDD)

```gherkin
Scenario: Cấp thêm dung lượng lưu trữ cho phòng ban thành công
  Given Tenant "Acme Corp" có hạn mức 100 GB, đã cấp 40 GB cho các phòng ban khác
  And phòng ban "Phòng Kỹ thuật" đang có hạn mức 20 GB, đã dùng thực tế 10 GB
  When TenantAdmin điều chỉnh hạn mức "Phòng Kỹ thuật" thành 35 GB
  Then hệ thống cập nhật StorageQuotaBytes của phòng thành 35 GB
  And hệ thống ghi AuditLog lưu vết thay đổi từ 20 GB lên 35 GB
  And phản hồi DepartmentStorageQuotaDto trả về thông tin hạn mức mới chính xác

Scenario: Từ chối hạ hạn mức thấp hơn tổng quota đã cấp cho các thành viên
  Given phòng ban "Phòng Kinh doanh" có tổng hạn mức cấp cho các thành viên là 25 GB
  When TenantAdmin cố gắng điều chỉnh hạn mức phòng ban xuống 15 GB
  Then hệ thống hủy bỏ giao dịch
  And trả về lỗi: "Không thể hạ hạn mức phòng ban xuống thấp hơn tổng hạn mức đã cấp cho các thành viên trong phòng (25 GB). Hãy thu hồi bớt hạn mức của thành viên trước."
  And hạn mức của phòng ban giữ nguyên không đổi
```

---

## 7. Out of Scope

- Tự động thu hồi quota của từng cá nhân khi hạ quota phòng ban (TenantAdmin hoặc DepartmentAdmin phải chủ động điều chỉnh từng thành viên trước).

---

## 8. Feature Lock

- **Locked Scope:** Nghiệp vụ kiểm tra 3 lớp và điều phối quota phòng ban (`AllocateDepartmentStorageQuotaCommand`).
- **Change Path:** `/sdd-update --feature=allocate-department-storage-quota --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `allocate-department-storage-quota` only.

---

## 9. AI Recommendation và Human Final Review

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature allocate-department-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
