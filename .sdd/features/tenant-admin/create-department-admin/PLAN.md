# PLAN: Create Department Admin (Bổ nhiệm Trưởng phòng Ban)

- **Feature Slug:** `create-department-admin`
- **Module:** `TenantAdmin`
- **Spec Version:** `1.1.0`
- **Status:** `APPROVED`
- **Spec Depth Baseline:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Risk Posture: `HIGH` (Bảo mật tài khoản, quản lý quota tạm thời, phân quyền lãnh đạo phòng ban).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Architectural Approach

Áp dụng mô hình **Clean Architecture + CQRS** qua thư viện **MediatR**:
- **Interface Layer (`CleanArchCqrs.API`):** `DepartmentAdminsController` tiếp nhận HTTP Request từ client, trích xuất `tenantId` từ route và ánh xạ payload sang `CreateDepartmentAdminCommand`. Không chứa business logic và không truy cập DB trực tiếp.
- **Application Layer (`CleanArchCqrs.Application`):**
  - `CreateDepartmentAdminCommand`: Immutable record định nghĩa dữ liệu đầu vào, **loại bỏ trường `Password`**.
  - `CreateDepartmentAdminCommandValidator`: Sử dụng FluentValidation để kiểm tra ràng buộc dữ liệu (bỏ quy tắc kiểm tra mật khẩu).
  - `CreateDepartmentAdminCommandHandler`: Điều phối toàn bộ logic nghiệp vụ (kiểm tra email active, giải phóng email của tài khoản xóa mềm, xác thực Tenant/Department, kiểm tra quota khả dụng, khởi tạo User `IsActive = false`, sinh `EmailVerificationToken` 48 giờ, ghi AuditLog, và gửi email qua `IEmailService`).
- **Domain Layer (`CleanArchCqrs.Domain`):** Sử dụng các thực thể nghiệp vụ: `User`, `Role`, `UserRole`, `Tenant`, `Department`, `UserStorageQuota`, `EmailVerificationToken`, `AuditLog`.
- **Infrastructure Layer (`CleanArchCqrs.Infrastructure`):** Triển khai persistence qua EF Core (`IApplicationDbContext`), băm mật khẩu ngẫu nhiên (`IPasswordHasher` bằng BCrypt), và dịch vụ gửi email (`IEmailService`).

---

## 2. REQ-to-Component Mapping

| Requirement | Component | Layer | File Path | Trách nhiệm |
| :--- | :--- | :--- | :--- | :--- |
| **EARS-01** | `CreateDepartmentAdminCommandHandler` | Application | `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs` | Khởi tạo User với `IsActive = false`, sinh mật khẩu ngẫu nhiên tạm thời (GUID băm BCrypt), gán role `DepartmentAdmin`, tạo `UserStorageQuota`, sinh `EmailVerificationToken` 48 giờ, gửi email mời qua `IEmailService`, ghi `AuditLog`. |
| **EARS-01A** | `CreateDepartmentAdminCommandHandler` | Application | `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs` | Kiểm tra tài khoản cũ có `IsActive = false`, đổi tên email cũ sang `{email}__deleted_{timestamp}` để giải phóng Unique Index, cho phép tạo mới tài khoản với email gốc mà không báo lỗi. |
| **EARS-02** | `CreateDepartmentAdminCommandHandler` | Application | `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs` | Liên kết Trưởng phòng với phòng ban chỉ định nếu `DepartmentId` có giá trị hợp lệ. |
| **EARS-03** | `CreateDepartmentAdminCommandHandler` | Application | `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs` | Hỗ trợ mô hình Self-Setup khi `DepartmentId = null`, cấp trước quota vào `UserStorageQuota` lấy từ dung lượng khả dụng của Tenant. |
| **EARS-04** | `CreateDepartmentAdminCommand`, `DepartmentAdminsController`, `CreateDepartmentAdminCommandValidator` | Application & Interface | `CreateDepartmentAdminCommand.cs`, `DepartmentAdminsController.cs`, `CreateDepartmentAdminCommandValidator.cs` | Bãi bỏ hoàn toàn trường `Password` trong Command, Request DTO và Validator; bắt buộc trải qua luồng xác thực email. |
| **EARS-05** | `CreateDepartmentAdminCommandHandler` | Application | `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs` | Từ chối tạo tài khoản nếu email đã tồn tại và đang ở trạng thái hoạt động (`IsActive = true`). |
| **EARS-06..10** | `CreateDepartmentAdminCommandHandler` | Application | `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs` | Kiểm tra tính hợp lệ của Tenant, Department, kiểm tra phòng ban chưa có Trưởng phòng (1:1), kiểm tra trần Quota của Department và Tenant. |

---

## 3. Detailed Components

### 3.1. Command DTO (`CreateDepartmentAdminCommand.cs`)
- **Vị trí:** `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommand.cs`
- **Thay đổi:** Loại bỏ thuộc tính `public string? Password { get; init; }`.
- **Cấu trúc mới:**
  ```csharp
  public record CreateDepartmentAdminCommand : IRequest<Result<UserDto>>
  {
      public Guid TenantId { get; init; }
      public Guid? DepartmentId { get; init; }
      public string Email { get; init; } = string.Empty;
      public string FirstName { get; init; } = string.Empty;
      public string LastName { get; init; } = string.Empty;
      public string? PhoneNumber { get; init; }
      public long? StorageQuotaBytes { get; init; }
      public long? AdminPersonalQuotaBytes { get; init; }
  }
  ```

### 3.2. Validator (`CreateDepartmentAdminCommandValidator.cs`)
- **Vị trí:** `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandValidator.cs`
- **Thay đổi:** Xóa bỏ rule `RuleFor(v => v.Password)`. Giữ nguyên validation cho `TenantId`, `Email`, `FirstName`, `LastName`, `PhoneNumber`.

### 3.3. Command Handler (`CreateDepartmentAdminCommandHandler.cs`)
- **Vị trí:** `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs`
- **Các bước thực thi cập nhật:**
  1. **Bước 1 (Email Validation & Soft-delete Re-creation):**
     - Truy vấn `_context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail)`.
     - Nếu user tồn tại và `IsActive == true` -> trả lỗi `Result<UserDto>.Failure($"Email '{request.Email}' đã tồn tại trong hệ thống.")`.
     - Nếu user tồn tại và `IsActive == false` -> Giải phóng email cũ bằng cách đổi thành `{baseEmail}__deleted_{timestamp}`.
  2. **Bước 2 & 3:** Xác thực Tenant active và Department active (nếu có truyền).
  3. **Bước 4:** Kiểm tra phòng ban chưa có Trưởng phòng (1-1) và tính toán Quota khả dụng.
  4. **Bước 5:** Tìm role `DepartmentAdmin`.
  5. **Bước 6 (Khởi tạo User):**
     - Sinh mật khẩu ngẫu nhiên tạm thời: `var initialPassword = Guid.NewGuid().ToString("N");`
     - Băm bằng `_passwordHasher.HashPassword(initialPassword)`.
     - Khởi tạo `User` với `IsActive = false`, `EmailConfirmed = false`.
  6. **Bước 7 & 8:** Gán role `DepartmentAdmin` và tạo `UserStorageQuota` (ví tạm giữ quota).
  7. **Bước 9 (Token Kích hoạt):**
     - Luôn sinh `tokenString = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")`.
     - Tạo `EmailVerificationToken` (loại `"DepartmentAdminInvitation"`, hết hạn 48 giờ).
  8. **Bước 10:** Ghi `AuditLog` với `Action = "InviteDepartmentAdminWithQuota"`.
  9. **Bước 11:** Lưu DB (`SaveChangesAsync`) và gửi thư mời qua `_emailService.SendDepartmentAdminInvitationAsync`. Bọc `try/catch` để lỗi SMTP không làm rollback DB.
  10. **Bước 12:** Trả về `Result<UserDto>.Success(...)`.

### 3.4. API Controller & Request DTO (`DepartmentAdminsController.cs`)
- **Vị trí:** `CleanArchCqrs.API/Controllers/Tenant/DepartmentAdminsController.cs`
- **Thay đổi:** Bỏ tham số `string? Password` trong `CreateDepartmentAdminRequest`, không gán `Password` vào Command.

---

## 4. Data Flow

```text
TenantAdmin (Client)
  │
  ▼ [POST /api/tenant/{id}/department-admins] (Payload không có Password)
DepartmentAdminsController
  │
  ▼ Send(CreateDepartmentAdminCommand)
MediatR Pipeline ──► ValidationBehavior (CreateDepartmentAdminCommandValidator)
  │
  ▼
CreateDepartmentAdminCommandHandler
  ├─► Check Email (Active vs Soft-deleted) ──► Đổi tên email cũ nếu IsActive = false
  ├─► Validate Tenant & Department
  ├─► Check Quota Limits & Existing DeptAdmin (1:1)
  ├─► Generate Temp Random Password & Hash (BCrypt)
  ├─► Create User (IsActive = false, EmailConfirmed = false)
  ├─► Add Role (DepartmentAdmin) & UserStorageQuota
  ├─► Generate EmailVerificationToken (48h)
  ├─► SaveChangesAsync (DB Transaction)
  ├─► Send Email Invitation via IEmailService (Catch SMTP exceptions)
  │
  ▼
Result<UserDto> (Status: 200 OK)
```

---

## 5. State-Change Classification

- **Persistence Mutation:**
  - Cập nhật trường `Email` của bản ghi `User` cũ (nếu tài khoản cũ bị xóa mềm) sang `{email}__deleted_{timestamp}`.
  - Thêm mới 1 bản ghi `User` (`IsActive = false`).
  - Thêm mới 1 bản ghi `UserRole` (gán quyền DepartmentAdmin).
  - Thêm mới 1 bản ghi `UserStorageQuota` (lưu hạn mức ban đầu).
  - Thêm mới 1 bản ghi `EmailVerificationToken` (token kích hoạt 48h).
  - Thêm mới 1 bản ghi `AuditLog` (hành động mời Trưởng phòng).
- **External Side Effect:**
  - Gửi email SMTP chứa liên kết kích hoạt tài khoản tới hộp thư của Trưởng phòng ban.
- **Human Checkpoint:** `N/A` (Thao tác nghiệp vụ chuẩn có cơ chế kiểm tra lỗi và Audit Trail đầy đủ).

---

## 6. Shared-Contract Impact

- **API Contract Change (Breaking Change nhẹ cho request payload):**
  - Endpoint: `POST /api/tenant/{tenantId}/department-admins`
  - Body: Loại bỏ trường `password` khỏi JSON payload.
  - Tương thích ngược: Backend bỏ trường này, nếu client cũ gửi thừa trường `password` thì JSON deserializer của ASP.NET Core sẽ tự động bỏ qua (không gây lỗi runtime).
- **Frontend Impact (`Cloud-DMS-FE`):**
  - File liên quan: `Cloud-DMS-FE/src/features/tenants/views/CreateDepartmentUserView.tsx` và `Cloud-DMS-FE/src/features/tenants/types/tenantUser.dto.ts`.
  - Cần loại bỏ ô nhập mật khẩu ("Mật khẩu ban đầu") trên giao diện tạo tài khoản Trưởng phòng và cập nhật giao diện hiển thị thông báo: *"Hệ thống sẽ gửi email mời kích hoạt tài khoản đến địa chỉ email này để Trưởng phòng tự thiết lập mật khẩu."*

---

## 7. Consistency Map

| Requirement | Plan Component | File Path | Verification Command | Post-code Review Trigger |
| :--- | :--- | :--- | :--- | :--- |
| `EARS-04` | Command DTO & Request DTO | `CreateDepartmentAdminCommand.cs`, `DepartmentAdminsController.cs` | `dotnet build Cloud-DMS/src/CleanArchCqrs.Application` | Review API Contract |
| `EARS-04` | Command Validator | `CreateDepartmentAdminCommandValidator.cs` | `dotnet build Cloud-DMS/src/CleanArchCqrs.Application` | Kiểm tra Validator rules |
| `EARS-01`, `EARS-01A`, `EARS-05` | Command Handler Logic | `CreateDepartmentAdminCommandHandler.cs` | `dotnet test Cloud-DMS/dotnet-clean-architecture-cqrs-starter.sln` | Security & Data Integrity Review |
| Toàn bộ EARS | Unit Tests | `CreateDepartmentAdminCommandHandlerTests.cs` | `dotnet test Cloud-DMS/tests/CleanArchCqrs.Tests` | Coverage & Regression Check |

---

## 8. Dependencies

1. **Phase 1 (Contract & DTOs):** Cập nhật `CreateDepartmentAdminCommand.cs` và `CreateDepartmentAdminRequest` trong controller.
2. **Phase 2 (Validation):** Cập nhật `CreateDepartmentAdminCommandValidator.cs`.
3. **Phase 3 (Core Business Logic):** Cập nhật `CreateDepartmentAdminCommandHandler.cs`.
4. **Phase 4 (Unit Testing):** Bổ sung bộ Unit Tests bao phủ tất cả kịch bản (kích hoạt qua email, tái tạo email xóa mềm, chặn email active, chặn trùng admin, kiểm tra quota).
5. **Phase 5 (Frontend Sync - Khuyến nghị):** Cập nhật FE view để ẩn/bỏ ô nhập mật khẩu.

---

## 9. Risks & Mitigations

### Risk 1: Xung đột độ dài trường Email khi giải phóng tài khoản xóa mềm
- **Mô tả:** Nếu email cũ có độ dài gần 256 ký tự, việc nối thêm hậu tố `__deleted_{timestamp}` có thể vượt quá độ dài tối đa của cột `Email` (VARCHAR(256)) gây lỗi DB exception.
- **Mitigation:** Áp dụng thuật toán cắt chuỗi an toàn như `CreateUserCommandHandler.cs`:
  ```csharp
  var suffix = $"__deleted_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
  var maxBaseLen = 256 - suffix.Length;
  var baseEmail = existingUser.Email.Length > maxBaseLen
      ? existingUser.Email[..maxBaseLen]
      : existingUser.Email;
  existingUser.Email = $"{baseEmail}{suffix}";
  ```

### Risk 2: Sự cố dịch vụ Email SMTP làm gián đoạn việc bổ nhiệm
- **Mô tả:** Nếu server SMTP gặp sự cố ngoại tuyến hoặc sai cấu hình, việc ném ngoại lệ có thể làm rollback giao dịch tạo Trưởng phòng.
- **Mitigation:** Bọc lệnh gọi `_emailService.SendDepartmentAdminInvitationAsync` trong khối `try/catch`. Ghi log cảnh báo nhưng không để ngoại lệ lan ra ngoài, bảo đảm bản ghi User và `EmailVerificationToken` đã được commit an toàn vào DB để quản trị viên có thể gửi lại link kích hoạt sau.

### Risk 3: Race condition khi hai yêu cầu cùng tạo Trưởng phòng cho một phòng ban
- **Mô tả:** Hai yêu cầu đồng thời có thể cùng vượt qua bước kiểm tra `existingDeptAdmin` trước khi commit.
- **Mitigation:** Kiểm tra chặt chẽ điều kiện trước khi commit; đảm bảo logic cập nhật diễn ra trong transaction DB nhất quán.

---

## 10. Questions for Human Reviewer

### Q1: Cập nhật giao diện Frontend (`Cloud-DMS-FE`)
- **Điều chưa rõ:** Yêu cầu hiện tại tập trung vào backend/spec `tenant-admin/create-department-admin`. Trên giao diện Frontend (`CreateDepartmentUserView.tsx`) hiện vẫn có form nhập "Mật khẩu ban đầu".
- **Quyết định của Human Reviewer:** *"Sau khi hoàn thành BE hãy chuyển qua FE để làm."*
- **Trạng thái:** `RESOLVED` (Kế hoạch thực thi: Triển khai hoàn chỉnh toàn bộ các task Backend T001–T005 trước, sau đó chuyển sang thực hiện các task Frontend T006–T008).

---

## 11. DoD (Definition of Done)

- [x] Kiến trúc tuân thủ Clean Architecture và CQRS qua MediatR.
- [x] Toàn bộ `EARS-01` đến `EARS-10` được ánh xạ đầy đủ vào các component cụ thể.
- [x] Loại bỏ trường `Password` khỏi DTO, Validator và quy trình tạo tài khoản.
- [x] Cơ chế tái tạo email xóa mềm (`IsActive = false`) đồng bộ 100% với `CreateUserCommandHandler.cs`.
- [x] Có ít nhất 3 rủi ro kỹ thuật và giải pháp xử lý tương ứng.
- [x] Đã giải quyết đầy đủ câu hỏi mở cho Human Reviewer (`RESOLVED`).

---

## 12. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Đề xuất:** Bản kế hoạch kiến trúc PLAN.md v1.1.0 đã được Human Reviewer phê duyệt triển khai tuần tự: Backend trước, Frontend sau.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Thực hiện hoàn thành các task Backend trước, sau đó chuyển sang Frontend để đồng bộ giao diện và loại bỏ trường mật khẩu.
- **Timestamp:** 2026-10-13T14:55:00+07:00
