# SPEC: Create User (Tạo Người dùng Mới)

- **Feature Slug:** `create-user`
- **Module:** `SystemAdmin`
- **Version:** `1.1.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Nghiệp vụ cấp phát tài khoản mới, hỗ trợ tự động khởi tạo Tenant đi kèm mã TenantCode và gán Cloud Storage Provider ngay từ đầu; cho phép tái tạo tài khoản có email trùng với tài khoản đã bị xóa mềm phục vụ kiểm thử và tái cấp phát.
  - Risk Posture: `HIGH` (Bảo mật thông tin đăng nhập ban đầu, cấp phát tài nguyên đa tổ chức).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép `SystemAdmin` tạo tài khoản người dùng mới trong toàn hệ thống. Hỗ trợ 2 luồng:
1. Gán người dùng vào một Tenant đã có sẵn.
2. Đồng thời khởi tạo một `Tenant` mới (sinh mã `TenantCode` duy nhất), gán vai trò quản trị viên Tenant cho người dùng và cấp phát `StorageProvider` kèm hạn mức dung lượng ban đầu trong cùng một transaction.
3. **Cơ chế tái tạo email đã xóa mềm:** Cho phép tái tạo tài khoản với email đã từng tồn tại nếu tài khoản trước đó đã ở trạng thái bị xóa mềm (`IsActive = false`), giúp giảm thiểu việc cạn kiệt email thực tế trong quá trình phát triển, kiểm thử liên tục.

---

## 2. Actors & Roles
- **Actor:** `SystemAdmin` (Có quyền `SystemAdmin.Users.Create` hoặc `user.manage`).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`403 Forbidden`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi lệnh `CreateUserCommand` với email hợp lệ chưa tồn tại trong danh sách tài khoản đang hoạt động (`IsActive = true`), `THEN` hệ thống `SHALL` tạo mới bản ghi `User`, băm mật khẩu bằng BCrypt, gán vai trò (`RoleIds`) và trả về `UserDto`.
- **EARS-01A (Event-driven / Re-creation):** `WHERE` email người dùng đã tồn tại trong hệ thống nhưng tài khoản cũ đó đã bị xóa mềm (`IsActive = false`), `THEN` hệ thống `SHALL` cho phép tạo lại tài khoản với email này (bằng cách giải phóng email của bản ghi cũ sang dạng `{email}__deleted_{timestamp}` để tránh vi phạm Unique Index hoặc tái kích hoạt và cập nhật bản ghi cũ theo thông tin mới với `IsActive = true`), gán mật khẩu, vai trò và Tenant mới mà không báo lỗi trùng email.
- **EARS-02 (Optional):** `WHERE` request có `TenantName` và chưa có `TenantId`, hệ thống `SHALL` tạo mới thực thể `Tenant`, tự động sinh `TenantCode` duy nhất (tối đa 8 ký tự chữ cái/chữ số hoặc định dạng `T_XXXXXX`), và gán `TenantId` cho người dùng.
- **EARS-03 (Optional):** `WHERE` request có cả `TenantName`, `StorageProviderId` và `StorageQuotaBytes`, hệ thống `SHALL` kiểm tra dung lượng khả dụng của Provider và gán hạn mức ngay cho Tenant mới.
- **EARS-04 (Unwanted):** `IF` email người dùng đã tồn tại trong bảng `Users` và tài khoản đó đang hoạt động (`IsActive = true`) (không phân biệt chữ hoa/thường), `THEN` hệ thống `SHALL` từ chối và trả về `Result<UserDto>.Failure("A user with email '{request.Email}' already exists.")`.
- **EARS-05 (Unwanted):** `IF` `StorageProviderId` được truyền vào nhưng không tìm thấy Provider trong CSDL, `THEN` hệ thống `SHALL` trả về `Result<UserDto>.Failure("Không tìm thấy Cloud Storage Provider...")`.
- **EARS-06 (Unwanted):** `IF` `StorageQuotaBytes` vượt quá dung lượng khả dụng của Cloud Provider, `THEN` hệ thống `SHALL` trả về `Result<UserDto>.Failure("Vượt quá dung lượng khả dụng của Cloud Provider...")`.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Mật khẩu phải được băm an toàn bằng BCrypt trước khi ghi vào CSDL.
- **NFR-DATA-01:** Toàn bộ quá trình tạo User, Tenant và gán Quota phải diễn ra trong cùng một CSDL transaction (atomic).
- **NFR-DATA-02:** Bảo đảm không vi phạm Unique Index `Users.Email` khi xử lý tài khoản có email trùng với tài khoản đã bị xóa mềm.

---

## 5. Data Model & Error Contract
- **Command:** `CreateUserCommand(Email, Password, FirstName, LastName, PhoneNumber, TenantName, Plan, RoleIds, TenantId, StorageProviderId, StorageQuotaBytes)`
- **Response:** `Result<UserDto>`
- **Errors:**
  - `DuplicateEmail`: `"A user with email '{email}' already exists."` (chỉ áp dụng khi tài khoản đang `IsActive = true`)
  - `ProviderNotFound`: `"Không tìm thấy Cloud Storage Provider..."`
  - `QuotaExceeded`: `"Vượt quá dung lượng khả dụng của Cloud Provider..."`

---

## 6. Acceptance Criteria (BDD)

### Kịch bản 1: Tạo người dùng thành công và khởi tạo Tenant mới
```gherkin
Scenario: Tạo người dùng thành công và khởi tạo Tenant mới
  Given SystemAdmin cung cấp email "admin@company.com" và TenantName "Công Ty Mới"
  When hệ thống xử lý CreateUserCommand
  Then tạo thành công User với thông tin đã nhập
  And tạo thành công Tenant có mã TenantCode duy nhất
  And User được liên kết với Tenant vừa tạo
```

### Kịch bản 2: Cho phép tạo tài khoản có email trùng với tài khoản đã xóa mềm
```gherkin
Scenario: Cho phép tạo lại tài khoản với email của người dùng đã bị xóa mềm
  Given Người dùng "dev.tester@gmail.com" đã tồn tại trong CSDL nhưng ở trạng thái IsActive = false
  When SystemAdmin gửi CreateUserCommand với email "dev.tester@gmail.com"
  Then Hệ thống không trả về lỗi DuplicateEmail
  And Cho phép tạo mới hoặc tái kích hoạt tài khoản "dev.tester@gmail.com" với IsActive = true
  And Mật khẩu và vai trò mới được áp dụng thành công
```

---

## 7. Out of Scope
- Gửi email kích hoạt tài khoản có link xác thực OTP (thực hiện qua use case mời hoặc auth riêng).

---

## 8. Feature Lock
- **Locked Scope:** Nghiệp vụ khởi tạo User, Tenant đi kèm, kiểm tra quota Provider ban đầu, cơ chế cho phép tái sử dụng email đã xóa mềm.
- **Change Path:** `/sdd-update --feature=create-user --artifact=spec --bump=minor`
- **Lock Boundary:** Use case `create-user` only.

---

## 9. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md v1.1.0 cho feature create-user (Cho phép tạo lại email của tài khoản đã bị xóa mềm).
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
