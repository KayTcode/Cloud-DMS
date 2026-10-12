# PHASE 1 — 🔴 CHOT CUA (làm ngay, 2–3 ngày)

> Mục tiêu: đóng toàn bộ cánh cửa cho phép truy cập ẩn danh và làm lộ dữ liệu.
> **Không làm xong Phase 1 thì không được deploy bất cứ thứ gì.**
> Bug ID theo `review/00_REVIEW_TOAN_BO.md` mục 10.

---

## P1-01 — Thêm xác thực toàn cục (FallbackPolicy)

**Bug:** BUG-01 · **Ảnh hưởng:** toàn bộ 22 endpoint · **Ước lượng:** 0.5 ngày

### Sửa
`src/CleanArchCqrs.API/Program.cs` — thay dòng `builder.Services.AddAuthorization();` bằng:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Mọi endpoint PHẢI đăng nhập, trừ khi khai báo [AllowAnonymous]
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

Thêm `[Authorize]` (namespace `Microsoft.AspNetCore.Authorization`) ở:
- `Controllers/ApiControllerBase.cs` (class-level) → phủ Files, Admin, Tenant, Department controllers
- `Controllers/UserController.cs` (class-level) — các action `login`/`CreateUser` khai `[AllowAnonymous]`/`[Authorize(Policy=...)]` riêng

Giữ nguyên `[AllowAnonymous]` ở `AuthController` (login, validate-invitation, accept-invitation) — 3 endpoint này **phải** public.

### Nghiệm thu
- `GET /api/admin/users` không token → **401** (hiện đang 200 — xem `01_TEST_EVIDENCE.md` mục 1)
- `POST /api/files/upload` không token → **401**
- `POST /api/auth/login` không token → vẫn **200**

### Kiểm chứng
Xem `06_CHECKLIST_KIEM_NGHIEM.md` → **CL-A (Authentication)**

---

## P1-02 — Gắn policy quyền cho từng endpoint

**Bug:** BUG-01, BUG-06 · **Ảnh hưởng:** phân quyền thật sự · **Ước lượng:** 1 ngày

### Sửa
Hiện chỉ có 1 endpoint gắn `[Authorize(Policy=...)]`. Gắn đủ theo permission đã seed trong `DbInitializer`:

| Controller | Endpoint | Policy |
|---|---|---|
| `Admin/UsersController` | GET list | `user.read` |
| | POST create | `user.create` |
| | PUT update, PATCH status | `user.update` |
| | PUT roles | `role.manage` |
| | POST reset-password | `user.update` |
| | DELETE | `user.delete` |
| `Admin/TenantQuotaController` | GET/PUT | `storage.manage` |
| `Admin/StorageProvidersController` | toàn bộ | `storage.manage` |
| `Tenant/DepartmentsController` | GET | `department.read` |
| | PUT quota | `storage.manage` |
| `Tenant/DepartmentAdminsController` | POST | `user.create` |
| `Department/DepartmentSetupController` | POST setup | `department.create` |
| `Department/DepartmentQuotasController` | GET users | `user.read` |
| | PUT quota | `storage.manage` |
| `FilesController` | upload | `file.upload` |
| | GET list, download | `file.read` |
| | DELETE | `file.delete` |

**Sửa `Authorization/PermissionPolicyProvider.cs`**: hiện nó trả policy cho *mọi* tên kể cả khi không tồn tại → gọi `base.GetPolicyAsync(policyName)` trước, chỉ tạo policy động khi base trả null:

```csharp
public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
{
    var existing = await base.GetPolicyAsync(policyName);
    if (existing != null) return existing;
    return new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("permission", policyName)
        .Build();
}
```

### Nghiệm thu
- Employee (không có quyền `user.create`) gọi `POST /api/admin/users` → **403**
- TenantAdmin gọi `GET /api/admin/users` → **200**

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-B (Authorization)**

---

## P1-03 — Xóa nhánh impersonation trong upload

**Bug:** BUG-02 · **Ảnh hưởng:** ghi ẩn danh / tính quota của người khác · **Ước lượng:** 0.5 ngày

### Sửa
1. `Features/Files/Commands/UploadFile/UploadFileCommand.cs` — **xóa** 3 property:
   `TenantIdOverride`, `UserIdOverride`, `StorageProviderIdOverride`
2. `Controllers/FilesController.cs` — **xóa** 3 tham số `[FromForm]`: `tenantId`, `storageProviderId`, `userId`
3. `UploadFileCommandHandler.cs` — **xóa toàn bộ** 2 khối fallback:
   - khối `if (user == null && tenantId.HasValue)` (lấy user đầu tiên của tenant)
   - khối `if (user == null)` → `FirstOrDefaultAsync(u => u.IsActive)` (lấy user đầu tiên CSDL)

   Thay bằng: nếu `_currentUserService.UserId` là null → **return 401 ngay** (ở tầng controller đã chặn, đây là lưới an toàn thứ 2):
   ```csharp
   var userId = _currentUserService.UserId;
   if (userId is null)
       return Result<FileEntryDto>.Failure("Chưa xác thực danh tính.");
   ```
   và `tenantId` luôn lấy từ `_currentUserService.TenantId`, không nhận từ request.

### Nghiệm thu
- `POST /api/files/upload` không token, có `userId=`/`tenantId=` → **401** (không còn đi tới bước check quota)
- Upload với token hợp lệ → vẫn hoạt động bình thường

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-C (Upload)**

---

## P1-04 — Chốt phạm vi tenant khi đọc/xóa file (IDOR)

**Bug:** BUG-03, BUG-04 · **Ảnh hưởng:** rò rỉ & phá file xuyên tenant · **Ước lượng:** 1 ngày

### Sửa
Inject `ICurrentUserService` và so sánh scope ở **mọi** chỗ chạm `FileEntries` theo Id:

1. `Features/Files/Queries/DownloadFile/DownloadFileQuery.cs`
   ```csharp
   if (fileEntry.TenantId != _currentUserService.TenantId)
       return Failure("Tệp tin không tồn tại.");   // 404-style, không lộ sự tồn tại
   ```
2. `Features/Files/Commands/DeleteFile/DeleteFileCommand.cs` — thêm check tương tự **trước** khi trừ quota
3. `Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs`:
   - **Bỏ** `request.TenantId` từ client (xóa property khỏi query hoặc chỉ dùng khi caller là SystemAdmin)
   - Thêm scope phòng ban: nếu caller không có role `SystemAdmin`/`TenantAdmin` → `Where(f => f.DepartmentId == currentUser.DepartmentId)`

> `ICurrentUserService` hiện **chưa có `DepartmentId`** → thêm property đọc claim `departmentId` (đã có trong JWT, xem `JwtTokenService`).

### Nghiệm thu
- User tenant A gọi `GET /api/files/{id của tenant B}/download` → **400/404**, không trả file
- Employee gọi `GET /api/files` → chỉ thấy file phòng ban của mình

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-D (Tenant isolation)**

---

## P1-05 — Sửa IDOR cấp quota: tôn trọng `departmentId` của route

**Bug:** BUG-05 · **Ảnh hưởng:** cấp quota chéo phòng/tenant · **Ước lượng:** 0.5 ngày

### Sửa
- `Controllers/Department/DepartmentQuotasController.cs`: truyền `departmentId` vào command
  ```csharp
  var command = new AllocateUserStorageQuotaCommand(departmentId, userId, request.QuotaBytes);
  ```
- `Features/DepartmentAdmin/Commands/AllocateUserStorageQuota/AllocateUserStorageQuotaCommand.cs`:
  - Thêm `Guid DepartmentId` vào record
  - Kiểm tra `user.DepartmentId == request.DepartmentId` → sai thì Failure
  - Kiểm tra caller (`_currentUserService.DepartmentId`) **== `request.DepartmentId`** → sai thì Failure (DepartmentAdmin không được cấp quota cho phòng khác)
- Cùng logic với `GetDepartmentUsersStorageQuotaQuery` (hiện cũng nhận `departmentId` nhưng không verify caller)

### Nghiệm thu
- DepartmentAdmin phòng X gọi `PUT /api/department/{phòng Y}/quota/users/{id}` → **403/Failure**
- Không truyền đúng `departmentId` của user → Failure

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-B**

---

## P1-06 — Xoá & thay secret đã lộ trong git

**Bug:** BUG-07, BUG-14 · **Ảnh hưởng:** cướp tài khoản email/JWT/cloud · **Ước lượng:** 0.5 ngày

### Sửa
1. **ĐỔI NGAY (ngoài repo):**
   - App password Gmail `laiw yryo opsr oist` → vào Google Account → revoke, tạo mới
   - JWT SecretKey → sinh 64 hex mới
   - MySQL: đổi user `root`, tạo user riêng cho app với quyền tối thiểu
2. `appsettings.json`: xoá giá trị secret thật, để trống hoặc xóa key
3. Bật cơ chế secret:
   - dev: `dotnet user-secrets init` + `dotnet user-secrets set "JwtSettings:SecretKey" ...`
   - prod: Env var `JwtSettings__SecretKey`, `EmailSettings__Password`, `ConnectionStrings__DefaultConnection`
4. **Xóa fallback hard-code:**
   - `Program.cs`: `?? "DefaultSuperSecretKey..."` → **throw** nếu thiếu
   - `Services/AesEncryptionService.cs`: `?? "CloudDMS-SuperSecure..."` → **throw**
   - `DependencyInjection/InfrastructureServiceExtensions.cs`: hàm `AddInfrastructureServices(string?)` với `root/root` → **xóa hẳn** (dead code, đã có bản nhận `IConfiguration`)
5. `Features/SystemAdmin/Commands/ExchangeOneDriveCode/ExchangeOneDriveCodeCommand.cs`: **không trả** `RefreshToken`, `AccessToken`, `ConfigJson` (chứa `client_secret`) trong response → chỉ trả `Success/Message/TotalCapacity...`
6. Thêm `appsettings.*.json` vào `.gitignore` nếu chứa secret; thêm bước quét secret (Phase 4: gitleaks)

### Nghiệm thu
- `git grep -i "laiw yryo\|DefaultSuperSecretKey\|CloudDMS-SuperSecure\|Password=root"` → **0 kết quả** (trừ file plan/review này)
- Server khởi động khi thiếu `JwtSettings:SecretKey` → **throw rõ ràng**, không chạy với key mặc định
- Response `POST /api/admin/storage-providers/onedrive/exchange-code` không còn chứa secret

---

## P1-07 — Chống Path Traversal + giới hạn upload

**Bug:** BUG-13 · **Ảnh hưởng:** ghi file ngoài thư mục, DoS bộ nhớ · **Ước lượng:** 0.5 ngày

### Sửa
1. `Services/Storage/LocalStorageService.cs`:
   ```csharp
   // Chỉ lấy tên file thuần, loại bỏ mọi đường dẫn do client gửi
   var safeName = Path.GetFileName(fileName);
   safeName = string.Join("_", safeName.Split(Path.GetInvalidFileNameChars()));
   var uniqueFileName = $"{Guid.NewGuid()}_{safeName}";
   // Chống traversal: xác nhận đường dẫn nằm trong _basePath
   var full = Path.GetFullPath(filePath);
   if (!full.StartsWith(Path.GetFullPath(_basePath), StringComparison.Ordinal))
       throw new InvalidOperationException("Đường dẫn lưu trữ không hợp lệ.");
   ```
   Áp dụng cả cho `DownloadFileAsync`/`DeleteFileAsync`/`CreateFolderAsync` (kiểm tra `storageKey` không chứa `..`).
2. `Controllers/FilesController.cs`:
   - `[RequestSizeLimit(...)]` hợp lý (ví dụ 100MB/file) + giới hạn số file mỗi request (vd. 20)
   - Validate extension/content-type whitelist (đen: `.exe .bat .cmd .js .php ...` hoặc whitelist văn phòng)
   - Không dựa vào `item.Length` của client cho quota — dùng `stream.Length` sau khi đọc (đã dùng `item.Length` của IFormFile, OK nhưng cần check `> 0` và `< max`)

### Nghiệm thu
- Upload file tên `../../evil.txt` → file nằm **bên trong** base path, tên được sanitize
- Upload 500MB → **413** với thông báo rõ

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-C**

---

## P1-08 — Global Exception Handler + ngừng lộ `ex.Message`

**Bug:** BUG-16 · **Ảnh hưởng:** lộ thông tin nội bộ, 500 mù · **Ước lượng:** 0.5 ngày

### Sửa
1. `Program.cs` — **đăng ký middleware** (hiện không có dòng nào!):
   ```csharp
   app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
   // đặt trước UseAuthentication
   ```
2. `Middleware/GlobalExceptionHandlerMiddleware.cs` — hiện là stub `throw;` → viết thật:
   - `ValidationException` (FluentValidation) → **400** + danh sách lỗi
   - `KeyNotFoundException` → 404
   - `UnauthorizedAccessException` → 401
   - `InvalidOperationException`/`DomainException` → 400 + message nghiệp vụ
   - còn lại → 500 với body generic `{ "error": "Đã xảy ra lỗi nội bộ." }`, **không trả `ex.Message`**
   - log full exception server-side (đã có `_logger.LogError`)
3. Xóa `catch (Exception ex) => StatusCode(500, ex.Message)` trong:
   - `AuthController.Login`, `AuthController.AcceptInvitation`
   - `UserController.Login` (và xem lại `StorageProvidersController.ConnectStorageProvider` bắt `Exception` → `400 + ex.Message`)
4. `Program.cs`: thêm `app.UseExceptionHandler(...)` **hoặc** middleware trên — chọn 1, không cần cả hai

### Nghiệm thu
- Gọi endpoint gây lỗi (vd `DELETE /api/files/{guid sai}` trên DB đang lệch) → trả JSON có cấu trúc, **không** có stack trace/ex.Message
- Gửi body thiếu trường bắt buộc của login → **400** (không phải 500)

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-E (Error handling)**

---

## ✅ CHECKPOINT SAU PHASE 1

- [ ] `dotnet build -c Release` → 0 error
- [ ] Chạy lại toàn bộ curl trong `review/01_TEST_EVIDENCE.md` → **mọi endpoint phải đổi sang 401** (trừ login/validate/accept invitation)
- [ ] Đã đổi app password Gmail + JWT key thật (ngoài repo)
- [ ] Không còn `*Override` trong `UploadFileCommand`
- [ ] Không còn dòng nào trả `ex.Message` ra client
