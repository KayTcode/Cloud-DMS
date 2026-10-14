# SPEC: Allocate User Storage Quota (Cấp phát & Điều chỉnh Hạn mức Nhân viên)

- **Feature Slug:** `allocate-user-storage-quota`
- **Module:** `DepartmentAdmin`
- **Version:** `1.1.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Điều phối hạn mức dung lượng lưu trữ cá nhân cho nhân viên trong phòng ban; bảo đảm giao dịch CSDL cô lập `Serializable`, bảo vệ dữ liệu đã sử dụng, thực thi ranh giới cô lập phòng ban và đa tổ chức (Multi-Tenant Isolation).
  - Risk Posture: `HIGH` (Bảo toàn hạn mức người dùng, chống Race Condition khi cấp phát đồng thời, chống Cross-Tenant IDOR).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `DepartmentAdmin` (hoặc `TenantAdmin` trong cùng tổ chức) tăng thêm, giảm bớt hoặc thu hồi hạn mức lưu trữ cá nhân (`UserStorageQuota`) cho một nhân viên cụ thể trực thuộc phòng ban của mình.
Bảo đảm các điều kiện biên then chốt:
1. Không được hạ hạn mức thấp hơn dung lượng thực tế nhân viên đã sử dụng (`userQuota.UsedBytes`).
2. Tổng hạn mức cấp phát cho các nhân viên khác cộng với hạn mức mới không được vượt quá tổng dung lượng của phòng ban (`department.StorageQuotaBytes`).
3. Thực thi nguyên tắc **Fail-Closed Authorization**: Bắt buộc cùng `TenantId` cho mọi role trừ SystemAdmin; bắt buộc đúng `DepartmentId` đối với Trưởng phòng (từ chối ngay khi DepartmentId là `null`).
4. Nhân viên được cấp hạn mức phải thuộc cùng phòng ban và cùng tổ chức với người gọi.
5. Bảo đảm Safe Error Contract (`ENG-02`): Không để lộ lỗi kỹ thuật CSDL ra ngoài.

---

## 2. Actors & Roles

- **Actor:** `DepartmentAdmin` (Chỉ được phép thao tác trên nhân viên thuộc đúng phòng ban của mình: `_currentUserService.DepartmentId == request.DepartmentId` và cùng `TenantId`).
- **Superiors:** `TenantAdmin` (Chỉ được phép điều chỉnh trong phạm vi tổ chức của mình: `department.TenantId == callerTenantId`), `SystemAdmin` (Có thể bypass kiểm tra Tenant/Phòng ban khi bảo trì).
- **Unauthorized:** Người dùng thuộc tổ chức khác hoặc phòng ban khác bị từ chối (`400 Bad Request` / `403 Forbidden`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` DepartmentAdmin gửi lệnh `AllocateUserStorageQuotaCommand(DepartmentId, UserId, QuotaBytes)` với hạn mức hợp lệ, `THEN` hệ thống `SHALL` mở transaction CSDL với mức cô lập `IsolationLevel.Serializable`, tạo mới hoặc cập nhật `UserStorageQuota`, ghi `AuditLog` và trả về `UserStorageQuotaDto`.
- **EARS-02 (Unwanted):** `IF` `QuotaBytes < 0`, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Hạn mức lưu trữ không được là số âm."`
- **EARS-03 (Unwanted):** `IF` phòng ban không thuộc cùng tổ chức với người gọi (`department.TenantId != _currentUserService.TenantId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Phòng ban không thuộc tổ chức của bạn."` (Cross-Tenant Protection).
- **EARS-04 (Unwanted):** `IF` người gọi không phải `SystemAdmin` hay `TenantAdmin` mà `DepartmentId` của người gọi là `null` hoặc không khớp với phòng ban mục tiêu (`callerDeptId != department.Id`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn không có quyền quản lý hạn mức của phòng ban này."` (Fail-Closed Department Isolation).
- **EARS-05 (Unwanted):** `IF` người dùng mục tiêu không tồn tại trong hệ thống, `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không tìm thấy thông tin người dùng."`
- **EARS-06 (Unwanted):** `IF` người dùng mục tiêu không thuộc cùng tổ chức với phòng ban (`user.TenantId != department.TenantId`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Người dùng không thuộc tổ chức này."` (Cross-Tenant User Isolation).
- **EARS-07 (Unwanted):** `IF` người dùng mục tiêu không thuộc phòng ban được chỉ định (`user.DepartmentId != department.Id`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Người dùng không thuộc phòng ban được chỉ định."`
- **EARS-08 (Unwanted):** `IF` hạn mức mới `QuotaBytes` nhỏ hơn dung lượng mà người dùng đã sử dụng (`userQuota.UsedBytes`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng ({FormatBytes(userQuota.UsedBytes)})."` (Bảo vệ dữ liệu).
- **EARS-09 (Unwanted):** `IF` tổng hạn mức đã cấp cho các thành viên khác cộng với `QuotaBytes` mới vượt quá dung lượng phòng ban (`availableInDept`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Phòng ban '{department.Name}' không còn đủ hạn mức để cấp cho nhân viên này! Hạn mức phòng ban còn lại: {FormatBytes(availableInDept)} (Yêu cầu: {FormatBytes(request.QuotaBytes)})."`
- **EARS-10 (Ubiquitous):** Hệ thống `SHALL` luôn che giấu các thông điệp ngoại lệ cơ sở dữ liệu nội bộ (`ex.Message`), ghi log chi tiết qua `ILogger` và trả về thông báo lỗi an toàn, chuẩn hóa: `"Đã xảy ra lỗi trong quá trình điều phối hạn mức lưu trữ. Vui lòng thử lại sau."` (`ENG-02`).
- **EARS-11 (Ubiquitous):** Hệ thống `SHALL` tự động ghi nhận bản ghi `AuditLog` với `Action = "AllocateUserQuota"`, lưu vết chi tiết giá trị hạn mức cũ và mới.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-RELIABILITY-01:** Bảo đảm tính nhất quán giao dịch và chống Race Condition qua `IsolationLevel.Serializable`.
- **NFR-RELIABILITY-02:** Bảo toàn dữ liệu người dùng: Tuyệt đối không cho phép hạ quota thấp hơn `UsedBytes`.
- **NFR-SEC-01 (Department Isolation):** Ngăn chặn 100% việc Trưởng phòng can thiệp quota của nhân viên phòng ban khác.
- **NFR-SEC-02 (Multi-Tenant Isolation):** Ngăn chặn 100% việc người dùng (kể cả TenantAdmin) can thiệp phòng ban thuộc tổ chức khác.
- **NFR-SEC-03 (Safe Error Contract):** Không để rò rỉ ngoại lệ DB engine hoặc stack trace ra bên ngoài client.

---

## 5. Data Model & Error Contract

- **Command:** `AllocateUserStorageQuotaCommand(Guid DepartmentId, Guid UserId, long QuotaBytes)`
- **Response:** `Result<UserStorageQuotaDto>`
- **HTTP Endpoint:** `PUT /api/department/{departmentId}/quota/users/{userId}` (Policy: `storage.manage`)

| Tình huống vi phạm | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- |
| QuotaBytes < 0 | `400 Bad Request` | `"Hạn mức lưu trữ không được là số âm."` |
| Phòng ban khác tổ chức | `400 Bad Request` | `"Phòng ban không thuộc tổ chức của bạn."` |
| Phòng ban khác phòng của mình | `400 Bad Request` | `"Bạn không có quyền quản lý hạn mức của phòng ban này."` |
| Người dùng không tồn tại | `400 Bad Request` | `"Không tìm thấy thông tin người dùng."` |
| Người dùng khác tổ chức | `400 Bad Request` | `"Người dùng không thuộc tổ chức này."` |
| Người dùng khác phòng ban | `400 Bad Request` | `"Người dùng không thuộc phòng ban được chỉ định."` |
| Hạ quota thấp hơn UsedBytes | `400 Bad Request` | `"Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng ({used})."` |
| Vượt quá hạn mức phòng ban | `400 Bad Request` | `"Phòng ban '{name}' không còn đủ hạn mức để cấp cho nhân viên này! ..."` |
| Lỗi CSDL nội bộ | `400 Bad Request` | `"Đã xảy ra lỗi trong quá trình điều phối hạn mức lưu trữ. Vui lòng thử lại sau."` |

---

## 6. Acceptance Criteria (BDD)

```gherkin
Scenario: Cấp thêm hạn mức cho nhân viên thành công
  Given phòng ban "Kỹ thuật" có hạn mức 50 GB, còn trống 15 GB
  And nhân viên "Nguyen Van A" có quota 5 GB, đã dùng 2 GB
  When Trưởng phòng điều chỉnh quota của "Nguyen Van A" thành 10 GB
  Then hệ thống cập nhật UserStorageQuota thành 10 GB
  And ghi nhận AuditLog chi tiết
  And trả về UserStorageQuotaDto thành công

Scenario: Từ chối hạ quota thấp hơn dung lượng nhân viên đã dùng
  Given nhân viên "Tran Van B" có quota 10 GB và đã tải lên 6 GB
  When Trưởng phòng cố gắng hạ quota xuống 4 GB
  Then hệ thống hủy bỏ giao dịch
  And trả về lỗi: "Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng (6 GB)."
  And quota của nhân viên vẫn giữ nguyên 10 GB

Scenario: Chặn TenantAdmin điều chỉnh quota của tổ chức khác
  Given TenantAdmin thuộc Tổ chức Alpha
  And phòng ban và nhân viên thuộc Tổ chức Beta
  When TenantAdmin Alpha gửi lệnh điều chỉnh quota cho nhân viên tại Tổ chức Beta
  Then hệ thống chặn thao tác
  And trả về lỗi "Phòng ban không thuộc tổ chức của bạn."
  And không có dữ liệu nào bị thay đổi
```

---

## 7. Out of Scope

- Tự động xóa file của nhân viên khi điều chỉnh quota.

---

## 8. Feature Lock

- **Locked Scope:** Logic kiểm tra và cấp phát trong `AllocateUserStorageQuotaCommandHandler`.
- **Change Path:** `/sdd-update --feature=allocate-user-storage-quota --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `allocate-user-storage-quota` only.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Ghi chú:** Bản đặc tả đã được cập nhật v1.1.0 đồng bộ với mã nguồn và kiểm định tự động đạt 100%.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả allocate-user-storage-quota v1.1.0.
- **Timestamp:** 2026-10-13T22:00:57+07:00

