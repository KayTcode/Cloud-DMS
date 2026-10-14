# SPEC: Create Employee (Tạo / Mời Nhân viên Phòng ban)

- **Feature Slug:** `create-employee`
- **Module:** `DepartmentAdmin`
- **Version:** `1.1.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Tạo tài khoản nhân viên mới trong phòng ban, cấp quota cá nhân ban đầu trích từ trần dung lượng khả dụng của phòng ban, kiểm định chặt chẽ đầu vào bằng FluentValidation, và kích hoạt tài khoản qua hệ thống Email Invitation an toàn 48h.
  - Risk Posture: `HIGH` (Bảo mật tài khoản, cấp phát dung lượng, xác thực vai trò, cô lập tổ chức và phòng ban).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `DepartmentAdmin` (hoặc `TenantAdmin` trong cùng tổ chức) bổ sung nhân sự mới (`Employee`) vào phòng ban:
- Hỗ trợ tạo tài khoản trực tiếp (khi có truyền mật khẩu) hoặc gửi thư mời kích hoạt qua Email (để trống mật khẩu) với `EmailVerificationToken` loại `EmployeeInvitation` hết hạn trong 48 giờ.
- Cấp trước một lượng hạn mức lưu trữ cá nhân ban đầu (`UserStorageQuota`) trích từ dung lượng chưa phân bổ của phòng ban (`availableInDept`).
- Bảo đảm an toàn schema bằng `CreateEmployeeCommandValidator` (FluentValidation) ngăn chặn payload độc hại hoặc quota âm.
- Bảo đảm nguyên tắc cô lập: Trưởng phòng chỉ được tạo nhân viên vào đúng phòng ban của mình; TenantAdmin chỉ được tạo trong tổ chức của mình.

---

## 2. Actors & Roles

- **Actor:** `DepartmentAdmin` (Chỉ tạo nhân viên cho đúng phòng ban mình quản lý: `callerDeptId.HasValue && callerDeptId.Value == department.Id`).
- **Superiors:** `TenantAdmin` (Chỉ tạo trong phòng ban thuộc tổ chức của mình: `department.TenantId == callerTenantId`), `SystemAdmin` (Toàn quyền).
- **Unauthorized:** Mọi vai trò khác bị từ chối (`400 Bad Request` / `403 Forbidden`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` DepartmentAdmin gửi `CreateEmployeeCommand` với thông tin hợp lệ và email chưa tồn tại, `THEN` hệ thống `SHALL` tạo thực thể `User`, gán vai trò `Employee`, gán `DepartmentId`, tạo bản ghi `UserStorageQuota`, ghi `AuditLog` và trả về `UserDto`.
- **EARS-02 (Ubiquitous):** Hệ thống `SHALL` kiểm tra tính hợp lệ của dữ liệu đầu vào qua `CreateEmployeeCommandValidator`:
  - `DepartmentId` không được để trống (`NotEmpty`).
  - `Email` không được để trống, đúng định dạng email RFC và tối đa 256 ký tự.
  - `FirstName` và `LastName` không được để trống và tối đa 100 ký tự.
  - `PhoneNumber` tối đa 25 ký tự (nếu có).
  - `StorageQuotaBytes` không được là số âm (`>= 0`).
- **EARS-03 (Optional):** `WHERE` mật khẩu (`Password`) để trống, hệ thống `SHALL` tạo mật khẩu tạm thời ngẫu nhiên, đặt `IsActive = false`, `EmailConfirmed = false`, sinh `EmailVerificationToken` loại `EmployeeInvitation` có hiệu lực 48 giờ và gửi email kích hoạt.
- **EARS-04 (Optional):** `WHERE` mật khẩu có giá trị, hệ thống `SHALL` băm mật khẩu, đặt `IsActive = true`, `EmailConfirmed = true` và không gửi email kích hoạt.
- **EARS-05 (Unwanted):** `IF` phòng ban không tồn tại, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Phòng ban với ID '{DepartmentId}' không tồn tại."`
- **EARS-06 (Unwanted):** `IF` phòng ban bị vô hiệu hóa (`!department.IsActive`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Phòng ban '{Name}' hiện đang bị vô hiệu hóa."`
- **EARS-07 (Unwanted):** `IF` Trưởng phòng tạo nhân viên cho phòng ban khác (`callerDeptId != department.Id`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn chỉ có quyền tạo nhân viên trong phòng ban của mình."`
- **EARS-08 (Unwanted):** `IF` TenantAdmin tạo nhân viên cho phòng ban thuộc tổ chức khác (`callerTenantId != department.TenantId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Phòng ban không thuộc tổ chức của bạn."`
- **EARS-09 (Unwanted):** `IF` email nhân viên đã tồn tại trên toàn hệ thống (không phân biệt hoa thường), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Email '{email}' đã tồn tại trong hệ thống."`
- **EARS-10 (Unwanted):** `IF` hạn mức cấp ban đầu `StorageQuotaBytes < 0`, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Hạn mức lưu trữ không được là số âm."`
- **EARS-11 (Unwanted):** `IF` hạn mức cấp ban đầu vượt quá dung lượng khả dụng của phòng ban (`availableInDept`), `THEN` hệ thống `SHALL` từ chối thao tác với thông báo: `"Hạn mức yêu cầu ({req}) vượt quá dung lượng khả dụng của phòng ban (Phòng còn trống: {avail}). Hãy cấp thêm dung lượng cho phòng ban trước."`
- **EARS-12 (Ubiquitous):** Lỗi gián đoạn gửi email `SHALL NOT` làm rollback giao dịch tạo tài khoản nhân viên trong CSDL; token kích hoạt vẫn được lưu an toàn trong CSDL để gửi lại sau.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Token Lifetime):** Token mời phải có thời gian sống chính xác 48 giờ và được mã hóa lưu trữ an toàn.
- **NFR-SEC-02 (Input Sanitization):** Toàn bộ chuỗi đầu vào (`Email`, `FirstName`, `LastName`, `PhoneNumber`) phải được cắt tỉa khoảng trắng (`Trim`) và chuẩn hóa an toàn chống injection.
- **NFR-RELIABILITY-01:** Bảo toàn hạn mức phòng ban: Tổng quota cấp cho các nhân viên không được vượt quá quota phòng ban.

---

## 5. Data Model & Error Contract

- **Command:**
  ```csharp
  public record CreateEmployeeCommand : IRequest<Result<UserDto>>
  {
      public Guid DepartmentId { get; init; }
      public string Email { get; init; } = string.Empty;
      public string FirstName { get; init; } = string.Empty;
      public string LastName { get; init; } = string.Empty;
      public string? PhoneNumber { get; init; }
      public string? Password { get; init; }
      public long? StorageQuotaBytes { get; init; }
  }
  ```
- **Response:** `Result<UserDto>`
- **HTTP Endpoint:** `POST /api/department/{departmentId}/employees` (Policy: `user.create`)

| Tình huống vi phạm | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- |
| Email / Tên / Họ trống hoặc sai định dạng | `400 Bad Request` | Theo thông điệp FluentValidation (`Email không được để trống`, v.v.) |
| QuotaBytes < 0 | `400 Bad Request` | `"Hạn mức lưu trữ không được là số âm."` |
| Phòng ban không tồn tại | `400 Bad Request` | `"Phòng ban với ID '{id}' không tồn tại."` |
| Phòng ban bị vô hiệu hóa | `400 Bad Request` | `"Phòng ban '{name}' hiện đang bị vô hiệu hóa."` |
| Trưởng phòng tạo phòng ban khác | `400 Bad Request` | `"Bạn chỉ có quyền tạo nhân viên trong phòng ban của mình."` |
| TenantAdmin tạo phòng ban khác tổ chức | `400 Bad Request` | `"Phòng ban không thuộc tổ chức của bạn."` |
| Email đã tồn tại | `400 Bad Request` | `"Email '{email}' đã tồn tại trong hệ thống."` |
| Quota vượt trần phòng ban | `400 Bad Request` | `"Hạn mức yêu cầu (...) vượt quá dung lượng khả dụng của phòng ban..."` |

---

## 6. Acceptance Criteria (BDD)

```gherkin
Scenario: Trưởng phòng mời nhân viên mới thành công
  Given phòng ban "Kỹ thuật" còn 10 GB dung lượng khả dụng
  When Trưởng phòng tạo nhân viên "dev@acme.com" với quota 2 GB không truyền mật khẩu
  Then hệ thống tạo User với IsActive = false, Role = "Employee"
  And cấp 2 GB vào UserStorageQuota (UsedBytes = 0)
  And sinh token EmployeeInvitation 48 giờ và gửi email kích hoạt
  And ghi nhận AuditLog
  And trả về UserDto thành công

Scenario: Chặn tạo nhân viên với hạn mức âm
  Given Trưởng phòng gửi yêu cầu tạo nhân viên
  When StorageQuotaBytes là -1024
  Then hệ thống chặn ngay tại tầng FluentValidation
  And trả về lỗi: "Hạn mức lưu trữ không được là số âm."

Scenario: Chặn Trưởng phòng tạo nhân viên vào phòng ban khác
  Given Trưởng phòng quản lý phòng ban A
  When Trưởng phòng gửi yêu cầu tạo nhân viên vào phòng ban B
  Then hệ thống từ chối
  And trả về thông báo lỗi: "Bạn chỉ có quyền tạo nhân viên trong phòng ban của mình."
```

---

## 7. Out of Scope

- Bổ nhiệm nhân viên làm Trưởng phòng (việc này thuộc thẩm quyền `TenantAdmin`).

---

## 8. Feature Lock

- **Locked Scope:** Logic tạo người dùng và cấp quota ban đầu trong `CreateEmployeeCommandHandler` và `CreateEmployeeCommandValidator`.
- **Change Path:** `/sdd-update --feature=create-employee --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `create-employee` only.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Ghi chú:** Bản đặc tả đã được cập nhật v1.1.0 tích hợp đầy đủ quy tắc FluentValidation, kiểm soát trần dung lượng, và phân quyền phòng ban/tổ chức.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả create-employee v1.1.0.
- **Timestamp:** 2026-10-13T22:15:00+07:00
