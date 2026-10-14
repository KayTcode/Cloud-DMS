# SPEC: Get Department Users Storage Quota (Báo cáo Hạn mức Nhân viên Phòng ban)

- **Feature Slug:** `get-department-users-storage-quota`
- **Module:** `DepartmentAdmin`
- **Version:** `1.2.0`
- **Status:** `REVISED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Truy vấn tổng hợp danh sách nhân viên trong phòng ban kèm thông tin hạn mức được cấp, dung lượng đã dùng, số lượng tệp tin đang lưu trữ, tổng hạn mức phân bổ và dung lượng còn trống của phòng ban; thực thi triệt để bảo mật cô lập phòng ban và Multi-Tenant Isolation theo nguyên tắc Fail-Closed.
  - Risk Posture: `HIGH` (Bảo vệ thông tin hạn mức, chống rò rỉ dữ liệu chéo tổ chức và chéo phòng ban).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `DepartmentAdmin` (hoặc `TenantAdmin` trong cùng tổ chức) xem bức tranh tổng thể về tình hình sử dụng dung lượng lưu trữ trong phòng ban:
- Tổng hạn mức phòng ban được cấp (`DepartmentQuotaBytes`).
- Tổng dung lượng file thực tế phòng ban đã sử dụng (`DepartmentUsedBytes`).
- Tổng hạn mức đã phân bổ cho các nhân viên (`TotalAllocatedToUsersBytes`).
- Hạn mức phòng ban còn lại chưa phân bổ cho ai (`UnallocatedDepartmentQuotaBytes = Max(0, DepartmentQuotaBytes - TotalAllocatedToUsersBytes)`).
- Danh sách chi tiết từng nhân viên active kèm: Tên, Email, Quota được cấp, Dung lượng đã dùng, Dung lượng khả dụng, Tỷ lệ phần trăm sử dụng (`UsagePercentage`) và **Số lượng tệp tin đang lưu trữ (`FileCount`)**.
- Thực thi nghiêm ngặt:
  1. **Multi-Tenant Isolation**: Bắt buộc cùng `TenantId` cho mọi role (ngoại trừ SystemAdmin).
  2. **Fail-Closed Department Isolation**: Đối với người dùng không phải TenantAdmin/SystemAdmin, nếu không có `DepartmentId` hoặc `DepartmentId != department.Id` thì lập tức từ chối.

---

## 2. Actors & Roles

- **Actor:** `DepartmentAdmin` (Chỉ xem phòng ban của chính mình: `callerDeptId.HasValue && callerDeptId.Value == department.Id` và cùng `TenantId`).
- **Superiors:** `TenantAdmin` (Chỉ được xem phòng ban thuộc tổ chức của mình: `department.TenantId == callerTenantId`), `SystemAdmin` (Toàn quyền).
- **Unauthorized:** Người dùng thuộc tổ chức khác hoặc phòng ban khác bị từ chối (`400 Bad Request` / `403 Forbidden`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` DepartmentAdmin gửi `GetDepartmentUsersStorageQuotaQuery(DepartmentId)`, `THEN` hệ thống `SHALL` truy vấn tất cả người dùng đang hoạt động (`IsActive = true`) trong phòng ban, nạp thông tin `UserStorageQuota`, tính toán các chỉ số tổng hợp và trả về `Result<DepartmentUsersQuotaSummaryDto>`.
- **EARS-02 (Unwanted):** `IF` phòng ban không tồn tại trong hệ thống, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Không tìm thấy thông tin Phòng ban."`
- **EARS-03 (Unwanted):** `IF` người gọi không phải `SystemAdmin` và phòng ban không thuộc cùng tổ chức với người gọi (`!callerTenantId.HasValue || department.TenantId != callerTenantId.Value`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Phòng ban không thuộc tổ chức của bạn."` (Multi-Tenant Isolation).
- **EARS-04 (Unwanted):** `IF` người gọi không phải `SystemAdmin` hay `TenantAdmin` mà `DepartmentId` của người gọi là `null` hoặc không khớp với phòng ban mục tiêu (`!callerDeptId.HasValue || callerDeptId.Value != department.Id`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn không có quyền xem thông tin hạn mức của phòng ban này."` (Fail-Closed Department Isolation).
- **EARS-05 (Ubiquitous):** Hệ thống `SHALL` tự động tính toán:
  - `TotalAllocatedToUsersBytes = Sum(UserStorageQuota.QuotaBytes)`
  - `UnallocatedDepartmentQuotaBytes = Math.Max(0, DepartmentQuotaBytes - TotalAllocatedToUsersBytes)`
  - `DepartmentUsedBytes = Sum(UserStorageQuota.UsedBytes)`
  - Từng nhân viên: `AvailableBytes = Math.Max(0, QuotaBytes - UsedBytes)` và `UsagePercentage = QuotaBytes > 0 ? Round(UsedBytes * 100.0 / QuotaBytes, 2) : 0`.
- **EARS-06 (Ubiquitous):** Hệ thống `SHALL` tự động thống kê tổng số lượng tệp tin hợp lệ chưa bị xóa (`!f.IsDeleted`) do từng nhân viên sở hữu (`OwnerId == user.Id`) trong bảng `FileEntries` và gán vào thuộc tính `FileCount` của từng `UserStorageQuotaDto`. Nếu nhân viên chưa tải lên tệp tin nào, `FileCount` `SHALL` nhận giá trị mặc định là `0`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-PERF-01:** Thời gian xử lý truy vấn phải **< 150ms** cho phòng ban có tối đa 500 thành viên. Truy vấn đếm số lượng tệp tin phải sử dụng phép nhóm `GroupBy(f => f.OwnerId)` tối ưu trên CSDL, không nạp toàn bộ danh thể file vào bộ nhớ.
- **NFR-SEC-01 (Department Isolation):** Ngăn chặn rò rỉ thông tin nhân sự, số lượng tệp và hạn mức giữa các phòng ban.
- **NFR-SEC-02 (Multi-Tenant Isolation):** Ngăn chặn TenantAdmin hoặc người dùng của tổ chức khác truy cập trái phép báo cáo hạn mức phòng ban.
- **NFR-SEC-03 (Fail-Closed):** Mặc định từ chối khi thông tin ngữ cảnh người dùng không đầy đủ (`DepartmentId == null`).

---

## 5. Data Model & Error Contract

- **Query:** `GetDepartmentUsersStorageQuotaQuery(Guid DepartmentId) : IRequest<Result<DepartmentUsersQuotaSummaryDto>>`
- **Response:** `Result<DepartmentUsersQuotaSummaryDto>`
- **HTTP Endpoint:** `GET /api/department/{departmentId}/quota/users` (Policy: `user.read`)

### Schema Chi Tiết:

#### 1. `DepartmentUsersQuotaSummaryDto`
| Thuộc tính | Kiểu dữ liệu | Mô tả |
| :--- | :--- | :--- |
| `DepartmentId` | `Guid` | Mã định danh phòng ban |
| `DepartmentName` | `string` | Tên phòng ban |
| `DepartmentQuotaBytes` | `long` | Tổng hạn mức của phòng ban (Bytes) |
| `DepartmentUsedBytes` | `long` | Tổng dung lượng thực tế phòng ban đã dùng (Bytes) |
| `TotalAllocatedToUsersBytes` | `long` | Tổng hạn mức đã chia cho các nhân viên (Bytes) |
| `UnallocatedDepartmentQuotaBytes` | `long` | Hạn mức phòng ban còn trống chưa phân bổ (Bytes) |
| `Users` | `List<UserStorageQuotaDto>` | Danh sách chi tiết hạn mức và tệp của từng nhân viên |

#### 2. `UserStorageQuotaDto`
| Thuộc tính | Kiểu dữ liệu | Mô tả |
| :--- | :--- | :--- |
| `UserId` | `Guid` | Mã định danh người dùng |
| `FullName` | `string` | Họ và tên nhân viên |
| `Email` | `string` | Địa chỉ email nhân viên |
| `DepartmentId` | `Guid` | Mã phòng ban trực thuộc |
| `DepartmentName` | `string` | Tên phòng ban trực thuộc |
| `QuotaBytes` | `long` | Hạn mức được cấp (Bytes) |
| `UsedBytes` | `long` | Dung lượng đã sử dụng (Bytes) |
| `FileCount` | `int` | **Số lượng tệp tin chưa xóa (`!IsDeleted`) đang lưu trữ** |
| `AvailableBytes` | `long` | Dung lượng khả dụng (`Max(0, QuotaBytes - UsedBytes)`) |
| `UsagePercentage` | `double` | Tỷ lệ sử dụng phần trăm (`Round(UsedBytes * 100.0 / QuotaBytes, 2)`) |

### Bảng Mã Lỗi (Error Contract):
| Tình huống vi phạm | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- |
| Phòng ban không tồn tại | `400 Bad Request` | `"Không tìm thấy thông tin Phòng ban."` |
| Phòng ban khác tổ chức | `400 Bad Request` | `"Phòng ban không thuộc tổ chức của bạn."` |
| Phòng ban khác phòng của mình | `400 Bad Request` | `"Bạn không có quyền xem thông tin hạn mức của phòng ban này."` |

---

## 6. Acceptance Criteria (BDD)

```gherkin
Scenario: Xem báo cáo hạn mức phòng ban thành công kèm số lượng tệp tin
  Given phòng ban "Kỹ thuật" có tổng quota 50 GB
  And có 3 nhân viên: 
    | User        | Quota | FileCount |
    | User 1      | 10 GB | 5 tệp tin |
    | User 2      | 15 GB | 0 tệp tin |
    | Trưởng phòng| 5 GB  | 12 tệp tin|
  When Trưởng phòng xem báo cáo hạn mức phòng ban
  Then hệ thống trả về TotalAllocatedToUsersBytes = 30 GB
  And UnallocatedDepartmentQuotaBytes = 20 GB
  And danh sách chứa đầy đủ thông tin của 3 người dùng
  And User 1 hiển thị FileCount = 5
  And User 2 hiển thị FileCount = 0
  And Trưởng phòng hiển thị FileCount = 12

Scenario: Chặn Trưởng phòng xem báo cáo của phòng ban khác
  Given Trưởng phòng quản lý phòng ban "Kế toán"
  When Trưởng phòng gửi yêu cầu xem báo cáo hạn mức của phòng "Kỹ thuật"
  Then hệ thống từ chối
  And trả về lỗi: "Bạn không có quyền xem thông tin hạn mức của phòng ban này."

Scenario: Chặn TenantAdmin xem phòng ban thuộc tổ chức khác
  Given TenantAdmin thuộc Tổ chức Alpha
  And phòng ban thuộc Tổ chức Beta
  When TenantAdmin Alpha gửi yêu cầu xem báo cáo hạn mức
  Then hệ thống từ chối
  And trả về lỗi: "Phòng ban không thuộc tổ chức của bạn."
```

---

## 7. Out of Scope

- Xuất báo cáo ra định dạng file Excel/PDF (tính năng export xử lý ở module báo cáo chuyên biệt).
- Phân tích chi tiết từng loại tệp (MIME type / định dạng tệp tin) - tính năng này thuộc về module báo cáo nâng cao.

---

## 8. Feature Lock

- **Locked Scope:** Logic truy vấn, thống kê dung lượng và đếm tệp trong `GetDepartmentUsersStorageQuotaQueryHandler`.
- **Change Path:** `/sdd-update --feature=get-department-users-storage-quota --artifact=spec --bump=minor`
- **Lock Boundary:** Feature `get-department-users-storage-quota` only.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Ghi chú:** Bản đặc tả đã được cập nhật lên v1.2.0 (Minor Bump) bổ sung trường `FileCount` vào `UserStorageQuotaDto`, bổ sung yêu cầu chức năng EARS-06 (thống kê số lượng tệp tin hợp lệ `!f.IsDeleted` theo từng nhân viên), cập nhật BDD Acceptance Criteria và Data Model Contract đồng bộ hoàn chỉnh với mã nguồn backend `CleanArchCqrs.Application` và frontend `Cloud-DMS-FE`.

### Human Final Review
- **Status:** `PENDING HUMAN REVIEW`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** 

