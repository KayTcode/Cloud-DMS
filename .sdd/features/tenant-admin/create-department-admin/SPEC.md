# SPEC: Create Department Admin (Bổ nhiệm Trưởng phòng Ban)

- **Feature Slug:** `create-department-admin`
- **Module:** `TenantAdmin`
- **Version:** `1.2.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Bổ nhiệm nhân sự lãnh đạo phòng ban (`DepartmentAdmin`) trong tổ chức theo quy trình **Invitation-First (Zero-Direct-Password)** bắt buộc; phân bổ ngân sách lưu trữ ban đầu (UserStorageQuota hoặc hạn mức phòng ban); kích hoạt tài khoản qua hệ thống Email Invitation bảo mật; hỗ trợ mô hình Self-Setup tự động phân bổ toàn bộ dung lượng khả dụng của Tenant vào ví tạm nếu không chỉ định hạn mức cụ thể.
  - Risk Posture: `HIGH` (Bảo mật tài khoản, quản lý quota tạm thời, phân quyền lãnh đạo phòng ban).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `TenantAdmin` bổ nhiệm một Trưởng phòng (`DepartmentAdmin`) mới cho tổ chức. Tính năng hỗ trợ các nguyên tắc cốt lõi:
1. **Quy trình Kích hoạt qua Thư mời (Invitation-First / Zero-Direct-Password):** TenantAdmin **không cần và không được tự nhập mật khẩu** cho Trưởng phòng. Hệ thống tự động khởi tạo tài khoản ở trạng thái chưa kích hoạt (`IsActive = false`, `EmailConfirmed = false`), sinh mật khẩu ngẫu nhiên tạm thời được băm an toàn, sinh mã xác thực `EmailVerificationToken` (loại `DepartmentAdminInvitation`, hết hạn trong 48 giờ) và gửi email mời kích hoạt qua `IEmailService`. Trưởng phòng tự kích hoạt và thiết lập mật khẩu cá nhân của mình qua luồng `AcceptInvitation` (tương tự quy trình tạo tài khoản từ `SystemAdmin`).
2. **Cơ chế tái tạo email đã xóa mềm (Re-creation / Soft-Delete):** Cho phép bổ nhiệm lại với email đã từng tồn tại trong hệ thống nếu tài khoản trước đó đã ở trạng thái bị xóa mềm (`IsActive = false`), giúp giảm thiểu việc cạn kiệt email thực tế trong quá trình phát triển và kiểm thử liên tục (đồng bộ với quy tắc tại `system-admin/create-user`).
3. **Hai mô hình phân bổ phòng ban:**
   - **Gán trực tiếp vào phòng ban có sẵn (`DepartmentId` có giá trị):** Kiểm tra phòng ban chưa có Trưởng phòng và cấp quota cá nhân không vượt quota khả dụng của phòng ban.
   - **Mô hình Tự thiết lập ("Admin-First / Self-Setup" - `DepartmentId` để trống):** Cấp trước một lượng quota từ tổ chức vào `UserStorageQuota` đóng vai trò là "ví giữ quota" tạm thời để Trưởng phòng sau khi kích hoạt có thể tự chạy lệnh khởi tạo phòng ban (`SetupDepartment`). Nếu TenantAdmin không chỉ định số quota cụ thể, hệ thống tự động cấp toàn bộ dung lượng khả dụng của Tenant vào ví tạm.

---

## 2. Actors & Roles

- **Actor:** `TenantAdmin` (Sở hữu quyền `user.create` trong phạm vi Tenant).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` TenantAdmin gửi lệnh `CreateDepartmentAdminCommand` với thông tin hợp lệ và email chưa tồn tại trong danh sách tài khoản đang hoạt động (`IsActive = true`), `THEN` hệ thống `SHALL` tạo mới bản ghi `User` với `IsActive = false`, `EmailConfirmed = false`, sinh mật khẩu ngẫu nhiên tạm thời (GUID) được băm an toàn bằng BCrypt, gán vai trò `DepartmentAdmin`, khởi tạo `UserStorageQuota`, sinh `EmailVerificationToken` (loại `DepartmentAdminInvitation`, hết hạn trong 48 giờ), gửi email mời kích hoạt qua `IEmailService`, ghi `AuditLog` và trả về `UserDto`.
- **EARS-01A (Event-driven / Re-creation):** `WHERE` email người dùng đã tồn tại trong hệ thống nhưng tài khoản cũ đó đã bị xóa mềm (`IsActive = false`), `THEN` hệ thống `SHALL` cho phép tạo lại tài khoản với email này (bằng cách giải phóng email của bản ghi cũ sang dạng `{email}__deleted_{timestamp}` để tránh vi phạm Unique Index), khởi tạo tài khoản mới ở trạng thái `IsActive = false`, gán mật khẩu ngẫu nhiên tạm thời, vai trò `DepartmentAdmin`, cấp `EmailVerificationToken` 48 giờ và gửi email mời kích hoạt mà không báo lỗi trùng email.
- **EARS-02 (Optional):** `WHERE` `DepartmentId` có giá trị hợp lệ, hệ thống `SHALL` liên kết Trưởng phòng với phòng ban đó.
- **EARS-03 (Optional):** `WHERE` `DepartmentId` để trống (Self-Setup), hệ thống `SHALL` tạo người dùng với `DepartmentId = null`, cấp `StorageQuotaBytes` vào `UserStorageQuota` lấy từ dung lượng khả dụng của Tenant (`tenant.StorageQuotaBytes - (existingDeptsQuota + pendingAdminsQuota)`). Nếu request không chỉ định quota cụ thể, hệ thống `SHALL` tự động cấp toàn bộ dung lượng khả dụng này vào ví tạm của Trưởng phòng, không được gán mặc định bằng 0.
- **EARS-04 (Mandatory Invitation / Passwordless Input):** Hệ thống `SHALL` loại bỏ việc nhập mật khẩu thủ công từ phía TenantAdmin; mọi tài khoản Trưởng phòng tạo mới đều bắt buộc trải qua luồng xác thực và kích hoạt email (`AcceptInvitation`) trước khi có thể đăng nhập.
- **EARS-05 (Unwanted / Duplicate Active Email):** `IF` email người dùng đã tồn tại trong CSDL và tài khoản đó đang hoạt động (`IsActive = true`) (không phân biệt chữ hoa/thường), `THEN` hệ thống `SHALL` từ chối và trả về lỗi: `"Email '{request.Email}' đã tồn tại trong hệ thống."`.
- **EARS-06 (Unwanted):** `IF` Tenant không tồn tại hoặc `IsActive = false`, `THEN` hệ thống `SHALL` từ chối và trả về lỗi: `"Tổ chức (Tenant) với ID '{id}' không tồn tại."` hoặc `"Tổ chức '{name}' hiện đang bị vô hiệu hóa."`
- **EARS-07 (Unwanted):** `IF` `DepartmentId` không thuộc Tenant hoặc phòng ban bị vô hiệu hóa, `THEN` hệ thống `SHALL` từ chối và trả về lỗi tương ứng.
- **EARS-08 (Unwanted):** `IF` phòng ban chỉ định đã có Trưởng phòng (`DepartmentAdmin`), `THEN` hệ thống `SHALL` từ chối với lỗi: `"Phòng ban '{name}' hiện đã có Trưởng phòng quản lý. Mỗi phòng ban chỉ được bổ nhiệm một Trưởng phòng."`
- **EARS-09 (Unwanted):** `IF` hạn mức cá nhân yêu cầu vượt quá dung lượng khả dụng của phòng ban, `THEN` hệ thống `SHALL` từ chối và báo lỗi: `"Hạn mức cá nhân yêu cầu ({FormatBytes(request.StorageQuotaBytes.Value)}) vượt quá dung lượng khả dụng của phòng ban '{department.Name}' (Phòng còn trống: {FormatBytes(availableInDept)})."`
- **EARS-10 (Unwanted):** `IF` hạn mức cấp trước (Self-Setup) vượt quá dung lượng khả dụng của Tenant, `THEN` hệ thống `SHALL` từ chối và báo lỗi: `"Hạn mức yêu cầu ({FormatBytes(quotaToAllocate)}) vượt quá dung lượng khả dụng của Tổ chức (Tenant còn trống: {FormatBytes(availableInTenant)})."`

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Bảo mật Token & Mật khẩu tạm):** Mật khẩu tạm thời phải là chuỗi ngẫu nhiên (GUID) được băm an toàn; Token mời phải có độ dài an toàn (chuỗi GUID kép 64 ký tự), hết hạn sau 48 giờ và đánh dấu sử dụng 1 lần duy nhất.
- **NFR-DATA-01 (Toàn vẹn dữ liệu Re-creation):** Bảo đảm không vi phạm Unique Index `Users.Email` khi xử lý tài khoản có email trùng với tài khoản đã bị xóa mềm (`IsActive = false`).
- **NFR-RELIABILITY-01 (Khả năng chịu lỗi SMTP):** Lỗi gửi email (SMTP failure) không được làm hủy giao dịch tạo người dùng trong CSDL; token vẫn được bảo toàn để có thể gửi lại link.
- **NFR-SEC-02 (Multi-Tenancy Isolation):** Không cho phép gán Trưởng phòng vào Tenant hoặc Department của tổ chức khác.

---

## 5. Data Model & Error Contract

- **Command:**
  ```csharp
  public record CreateDepartmentAdminCommand(
      Guid TenantId,
      Guid? DepartmentId,
      string Email,
      string FirstName,
      string LastName,
      string? PhoneNumber,
      long? StorageQuotaBytes,
      long? AdminPersonalQuotaBytes
  ) : IRequest<Result<UserDto>>;
  ```
- **Request DTO:**
  ```csharp
  public record CreateDepartmentAdminRequest(
      string Email,
      string FirstName,
      string LastName,
      string? PhoneNumber = null,
      Guid? DepartmentId = null,
      long? StorageQuotaBytes = null,
      long? AdminPersonalQuotaBytes = null);
  ```
- **Response:** `Result<UserDto>`
- **HTTP Endpoint:** `POST /api/tenant/{tenantId}/department-admins` (Policy: `user.create`)
- **Validation Rules:**
  - `Email`: Bắt buộc, định dạng email hợp lệ, tối đa 256 ký tự.
  - `FirstName`: Bắt buộc, tối đa 100 ký tự.
  - `LastName`: Bắt buộc, tối đa 100 ký tự.
  - `PhoneNumber`: Tùy chọn, tối đa 25 ký tự nếu có.
  - `Password`: Không còn xuất hiện trong Command/Request.
- **Errors:**
  - `DuplicateEmail`: `"Email '{email}' đã tồn tại trong hệ thống."` (chỉ áp dụng khi tài khoản đang `IsActive = true`)
  - `TenantNotFound`: `"Tổ chức (Tenant) với ID '{tenantId}' không tồn tại."`
  - `TenantInactive`: `"Tổ chức '{name}' hiện đang bị vô hiệu hóa."`
  - `DepartmentNotFound`: `"Phòng ban với ID '{departmentId}' không tồn tại trong tổ chức."`
  - `DepartmentInactive`: `"Phòng ban '{name}' hiện đang bị vô hiệu hóa."`
  - `DepartmentAlreadyHasAdmin`: `"Phòng ban '{name}' hiện đã có Trưởng phòng quản lý. Mỗi phòng ban chỉ được bổ nhiệm một Trưởng phòng."`
  - `DepartmentQuotaExceeded`: `"Hạn mức cá nhân yêu cầu ({FormatBytes(quota)}) vượt quá dung lượng khả dụng của phòng ban..."`
  - `TenantQuotaExceeded`: `"Hạn mức yêu cầu ({FormatBytes(quota)}) vượt quá dung lượng khả dụng của Tổ chức..."`

---

## 6. Acceptance Criteria (BDD)

### Kịch bản 1: Bổ nhiệm Trưởng phòng thành công qua Email Invitation (Mặc định không mật khẩu)
```gherkin
Scenario: Bổ nhiệm Trưởng phòng thành công qua Email Invitation
  Given TenantAdmin thuộc tổ chức "Acme Corp"
  And Tenant "Acme Corp" còn 20 GB khả dụng
  When TenantAdmin gửi yêu cầu bổ nhiệm Trưởng phòng cho "lead@acme.com" với quota 5 GB (không có trường mật khẩu)
  Then hệ thống tạo User với IsActive = false và EmailConfirmed = false
  And gán quyền DepartmentAdmin
  And cấp 5 GB vào UserStorageQuota
  And sinh EmailVerificationToken loại "DepartmentAdminInvitation" có hạn 48 giờ
  And gửi email kích hoạt tới "lead@acme.com"
  And trả về UserDto thành công
```

### Kịch bản 2: Cho phép bổ nhiệm lại với email của tài khoản đã bị xóa mềm
```gherkin
Scenario: Cho phép tạo lại tài khoản Trưởng phòng với email đã bị xóa mềm
  Given Người dùng "old.deptadmin@acme.com" đã tồn tại trong CSDL nhưng ở trạng thái IsActive = false
  When TenantAdmin gửi yêu cầu bổ nhiệm Trưởng phòng với email "old.deptadmin@acme.com"
  Then hệ thống không trả về lỗi trùng email
  And giải phóng email của bản ghi cũ sang dạng "old.deptadmin@acme.com__deleted_{timestamp}"
  And tạo mới tài khoản "old.deptadmin@acme.com" với IsActive = false
  And gửi thư mời kích hoạt mới kèm token 48 giờ tới "old.deptadmin@acme.com"
  And trả về UserDto thành công
```

### Kịch bản 3: Từ chối bổ nhiệm khi email đang hoạt động
```gherkin
Scenario: Từ chối bổ nhiệm khi email đã tồn tại và đang hoạt động
  Given Người dùng "active.lead@acme.com" đã tồn tại trong CSDL với IsActive = true
  When TenantAdmin gửi yêu cầu bổ nhiệm Trưởng phòng với email "active.lead@acme.com"
  Then hệ thống từ chối và trả về lỗi "Email 'active.lead@acme.com' đã tồn tại trong hệ thống."
```

### Kịch bản 4: Từ chối bổ nhiệm khi phòng ban đã có Trưởng phòng
```gherkin
Scenario: Từ chối bổ nhiệm khi phòng ban đã có Trưởng phòng
  Given phòng ban "Kỹ thuật" đã có Trưởng phòng "admin_kt@acme.com"
  When TenantAdmin bổ nhiệm thêm Trưởng phòng cho phòng "Kỹ thuật"
  Then hệ thống từ chối
  And trả về thông báo "Phòng ban 'Kỹ thuật' hiện đã có Trưởng phòng quản lý. Mỗi phòng ban chỉ được bổ nhiệm một Trưởng phòng."
```

---

## 7. Out of Scope

- TenantAdmin tự đặt mật khẩu cho DepartmentAdmin (đã bãi bỏ hoàn toàn; Trưởng phòng bắt buộc kích hoạt qua email invitation).
- Bổ nhiệm nhiều hơn 1 Trưởng phòng cho một phòng ban (hệ thống chỉ cho phép tỷ lệ 1-1).
- Kích hoạt tài khoản và đặt mật khẩu lần đầu (thực hiện qua use case `AcceptInvitation`).

---

## 8. Feature Lock

- **Locked Scope:** Logic xử lý `CreateDepartmentAdminCommandHandler`, validator `CreateDepartmentAdminCommandValidator`, và controller `DepartmentAdminsController`.
- **Change Path:** `/sdd-update --feature=create-department-admin --artifact=spec --bump=minor`
- **Lock Boundary:** Feature `create-department-admin` only.

---

## 9. Change Impact Record

- Changed intent, requirement, architecture, or task assumption:
  - Bãi bỏ việc TenantAdmin nhập mật khẩu trực tiếp cho DepartmentAdmin (chuyển sang 100% Email Invitation).
  - Bổ sung cơ chế giải phóng email của tài khoản đã xóa mềm (`IsActive = false`) để cho phép tái cấp phát (đồng bộ với `system-admin/create-user`).
  - Chuẩn hóa EARS-03 (v1.2.0): Khi `DepartmentId` để trống (Self-Setup), nếu `StorageQuotaBytes` không được chỉ định hoặc `<= 0`, hệ thống tự động cấp toàn bộ dung lượng khả dụng của Tenant (`availableInTenant`) vào `UserStorageQuota` của Trưởng phòng, loại bỏ hoàn toàn việc gán nhầm quota bằng 0.
  - Cập nhật EARS-01, EARS-01A, EARS-03, EARS-04, EARS-05, Command DTO, Request DTO và BDD Scenarios.
- Feature Lock boundary affected: Feature `create-department-admin` (Command, Handler, Validator, Controller, Request DTO).
- Material state-change classification: Behavior & Contract Update (Spec v1.2.0).
- Downstream artifacts invalidated: `PLAN.md`, `TASKS.md`, `CreateDepartmentAdminCommand`, `CreateDepartmentAdminRequest`, `CreateDepartmentAdminCommandValidator`, `CreateDepartmentAdminCommandHandler`.
- Required trace, test, and sync actions: Đồng bộ code backend (`CleanArchCqrs.Application` và `CleanArchCqrs.API`) và viết unit/integration test xác thực.
- Review follow-up: `Architecture & Security Review Mandatory`.

---

## 10. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Đề xuất:** Phê duyệt bản đặc tả SPEC.md v1.2.0 cho feature `create-department-admin` (100% Email Invitation, tái tạo email tài khoản xóa mềm, và tự động cấp quota khả dụng của Tenant khi Self-Setup theo EARS-03).

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả SPEC.md v1.2.0 cho feature create-department-admin.
- **Reviewed At:** 2026-10-14T00:30:00+07:00
- **Notes / Adjustments:** Phê duyệt toàn bộ cập nhật Self-Setup Quota allocation và bảo mật Invitation-First.
