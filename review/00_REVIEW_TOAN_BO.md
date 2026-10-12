# BÁO CÁO REVIEW TOÀN BỘ DỰ AN — Cloud-DMS (CleanArchCqrs)

- Ngày review: 10/10/2026
- Phạm vi: toàn bộ `src/` (4 project .NET 8), `docs/`, `README.md`
- Phương pháp: đọc code từng layer, truy vết luồng dữ liệu qua lại giữa các file, build Release, **và chạy thật server + pen-test endpoint** (xem `01_TEST_EVIDENCE.md`)
- Kết luận: chỉ đưa ra sau khi đã kiểm chứng bằng build hoặc bằng request thật; phần nào chưa kiểm chứng được sẽ ghi rõ độ tin cậy.

---

## 1. TONG QUAN HE THONG

**Đây là gì:** Backend Multi-Tenant Document Management System (Cloud-DMS), .NET 8, Clean Architecture + CQRS (MediatR), EF Core + MySQL, lưu file lên Local/Google Drive/OneDrive, phân quyền RBAC 4 role (SystemAdmin / TenantAdmin / DepartmentAdmin / Employee), quota phân cấp: StorageProvider → Tenant → Department → User.

**Cấu trúc thực tế:**

| Layer | Project | Nhận xét |
|---|---|---|
| API | `CleanArchCqrs.API` | 11 controller, 22 action |
| Application | `CleanArchCqrs.Application` | ~60 handler/query, 2 folder Features trùng lặp (`Features/` và `Users/`) |
| Domain | `CleanArchCqrs.Domain` | 17 entity, thuần POCO |
| Infrastructure | `CleanArchCqrs.Infrastructure` | EF Core, 3 dịch vụ storage, email, JWT, BCrypt, ClamAV |

**Quy mô code:** 192 file `.cs` (trừ obj/bin). **0 test file. 0 frontend. 0 CI. 0 Docker.**

**Điểm nổi bật ngay từ đầu:**
- README nói đây là *starter template* bán trên Patreon, **không khớp** với code thật (README bảo .NET 10, code là net8.0; README nói Product CRUD, code là DMS).
- `docs/` (5 file) mô tả endpoint **không khớp** với route thật trong code → xem mục 4.
- Code có 2 "vùng" phát triển chồng lên nhau: một bên viết tiếng Việt (Features/*), một bên viết tiếng Anh còn là stub (`Users/*`, `LoggingBehavior`, `GlobalExceptionHandlerMiddleware` — đều là stub "see Patreon").

---

## 2. NHUNG DIEM LAM TOT

| # | Điểm tốt | Bằng chứng |
|---|---|---|
| 1 | **Cấu trúc Clean Architecture đúng chuẩn**: dependency chỉ đi vào trong, tách 4 layer rõ ràng | `Application` không tham chiếu `Infrastructure`; `IApplicationDbContext`, `ICurrentUserService`… là interface ở Application |
| 2 | **CQRS + MediatR + FluentValidation pipeline** hoạt động đúng | `ValidationBehavior` tự chạy validator, trả `Result.Failure` thay vì ném exception |
| 3 | **Result pattern** thống nhất, không ném exception bừa ở tầng nghiệp vụ | `Result`/`Result<T>` có guard bắt buộc success không được kèm error |
| 4 | **BCrypt** cho password (không tự bịa hash) | `PasswordHasher.cs` |
| 5 | **Audit log** được ghi ở hầu hết thao tác quan trọng (upload, delete, cấp quota, setup phòng ban) | `AuditLogs.Add(...)` ở 8 handler |
| 6 | **Chống trùng email có index unique** | `UserConfiguration`: `HasIndex(u => u.Email).IsUnique()` |
| 7 | **Quota được kiểm tra nhiều lớp** (user/department/tenant/provider) và có logic "không được hạ quota dưới đã dùng" | `AllocateDepartmentStorageQuotaCommand`, `AllocateUserStorageQuotaCommand` |
| 8 | **Soft-delete file** + hoàn quota khi xóa (một phần) | `DeleteFileCommand` |
| 9 | **RBAC seed dữ liệu khá đầy đủ** permission theo role | `DbInitializer.SeedRolePermissionsAsync` |
| 10 | **Có khái niệm邀请 (invitation) token** kèm expiry 48h, đánh dấu đã dùng | `EmailVerificationToken` + `AcceptInvitationCommand` |
| 11 | **Message lỗi tiếng Việt rõ ràng, có số liệu** ("Đã dùng X / Hạn mức Y") — UX lỗi tốt | `UploadFileCommandHandler` |
| 12 | Build sạch: 0 error | `dotnet build -c Release` |

---

## 3. NHUNG LOI NGHIEM TRONG (CRITICAL)

> Toàn bộ mục này **đã kiểm chứng bằng request thật**, xem `01_TEST_EVIDENCE.md`.

### 🔴 C1. GẦN NHƯ TOÀN BỘ API KHÔNG CÓ XÁC THỰC / PHÂN QUYỀN
- **Sự thật:** trong toàn bộ solution chỉ có **đúng 1 attribute `[Authorize]`** (`UserController`, policy `user.create`). Không có `FallbackPolicy`, không có `RequireAuthenticatedUser`, không có filter toàn cục.
- `Program.cs` chỉ gọi `builder.Services.AddAuthorization();` — dòng này **không ép xác thực endpoint nào** khi không có attribute/fallback.
- **Hệ quả (đã test thật):** `GET /api/admin/users` → **200 kèm PII**; `GET /api/admin/users/{id}` → **200**; `GET /api/department/{id}/quota/users` → **200 kèm email cả phòng**; `POST .../reset-password`, `DELETE /api/files/{id}`, `PUT .../quota/users/{userId}` → **chạy thẳng vào handler, không trả 401 lần nào**.
- Ai cũng có thể: liệt kê user, đọc hồ sơ, **đổi mật khẩu user bất kỳ**, tạo tài khoản admin, cấp/thu hồi quota, xóa file, đọc file của tenant khác.
- **Cách sửa:** Thêm `[Authorize]` ở `ApiControllerBase`/mọi controller + `builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());` rồi gắn policy quyền theo từng endpoint.

### 🔴 C2. UPLOAD FILE: FALLBACK "LẤY USER ĐẦU TIÊN TRONG CSDL" + CHO PHÉP CLIENT CHỈNH `userId`/`tenantId`
- `FilesController.Upload` nhận `userId`, `tenantId`, `storageProviderId` từ **client** (`[FromForm]`).
- `UploadFileCommandHandler` dùng `request.UserIdOverride ?? currentUser`, và nếu vẫn null thì **`FirstOrDefault(u => u.IsActive)`** — tức là "user active đầu tiên của hệ thống".
- **Đã test:** upload không token, truyền `tenantId` → đi qua toàn bộ pipeline tới bước kiểm tra quota (không hề bị chặn ở xác thực).
- **Hậu quả:** ẩn danh có thể upload file tính vào quota của người khác, ghi file vào tenant khác, chọn storage provider tùy ý.
- **Cách sửa:** Bỏ toàn bộ `*Override`; luôn lấy identity từ JWT; xóa 2 khối fallback; ép `tenantId = currentUser.TenantId`.

### 🔴 C3. DOWNLOAD/DELETE FILE KHÔNG KIỂM TRA QUYỀN (IDOR xuyên tenant)
- `DownloadFileQueryHandler` và `DeleteFileCommandHandler` chỉ tra `FileEntries` theo `Id`, **không so sánh** `TenantId`/`DepartmentId`/`OwnerId` với caller.
- Kết hợp C1 → bất kỳ ai biết GUID là tải/xóa được file của **tenant khác**.
- **Cách sửa:** kiểm tra `fileEntry.TenantId == currentUser.TenantId` (và scope phòng ban cho Employee) trước khi xử lý; trả 404 thay vì 400 để không lộ sự tồn tại.

### 🔴 C4. THIẾU GLOBAL EXCEPTION HANDLER → LỘ THÔNG TIN LỖI + 500 MÙ
- `GlobalExceptionHandlerMiddleware.cs` tồn tại nhưng **không hề được đăng ký** (`app.UseMiddleware` không có trong `Program.cs`) → đúng như comment "stub".
- `AuthController` bắt `catch (Exception ex) => StatusCode(500, ex.Message)` → **trả nội dung exception cho client** (lộ stack/SQL/đường dẫn).
- Các controller khác không bắt → 500 không body; `ValidationException` (ném từ `ValidationBehavior` với response không phải `Result`) thành 500 thay vì 400.
- **Đã test:** `DELETE /api/files/{id}` trả 500 với `MySqlException` trong log.
- **Cách sửa:** đăng ký middleware (hoặc `AddProblemDetails` + `UseExceptionHandler`), map `ValidationException` → 400, không bao giờ trả `ex.Message` ở Production.

### 🔴 C5. MIGRATION HỖN LOẠN: `MigrateAsync` + ALTER TABLE RAW + `catch {}` NUỐT LỖI
- `Program.cs` (~150 dòng): `MigrateAsync()` bọc try/catch nuốt lỗi, rồi `ALTER TABLE ... ADD COLUMN` bọc `catch {}`, rồi `CREATE TABLE IF NOT EXISTS` thủ công.
- **Đã test thật:** môi trường hiện tại ném `Table 'Permissions' already exists` → migration thất bại bị nuốt → schema lệch → **`FileEntries.DepartmentId` không tồn tại** → mọi thao tác file trả **500**.
- Rủi ro: startup race khi chạy nhiều instance; cột mới trong migration mà không có trong danh sách ALTER sẽ không bao giờ được thêm; error nuốt mất khiến debug rất khó.
- **Cách sửa:** dùng đúng 1 nguồn sự thật là EF Migrations; với DB có sẵn thì sửa lại migration history hoặc `dotnet ef database update` có kiểm soát; xóa toàn bộ ALTER raw.

### 🔴 C6. SECRET ĐƯỢC COMMIT VÀO REPO
`appsettings.json` trong git chứa:
- App password Gmail thật: `laiw yryo opsr oist`
- JWT SecretKey thật
- MySQL `root/root`
Ngoài ra fallback hard-code: `secretKey ?? "DefaultSuperSecretKey..."`, `AesEncryptionService` fallback `"CloudDMS-SuperSecureEncryptionKey32Chars!"`, `InfrastructureServiceExtensions` fallback `root/root`.
- **Cách sửa:** đưa ra user-secrets/Env Var, **đổi ngay app password Gmail**, bật secret scanning (git-secrets/gitleaks), fallback phải `throw` thay vì dùng key mặc định.

---

## 4. NHUNG LOI CHUC NANG (đối chiếu docs ↔ code)

| # | Chức năng (theo docs) | Trạng thái | Chi tiết |
|---|---|---|---|
| F1 | `GET /api/systemadmin/audit-logs` (docs 01) | ❌ **Thiếu** | Không có controller/endpoint audit-log nào |
| F2 | `POST /api/departmentadmin/employees` — tạo nhân viên (docs 03) | ❌ **Thiếu** | Không có endpoint nào tạo Employee |
| F3 | Role Employee trong `POST /api/admin/users` | ❌ **Bug** | `CreateUserCommandHandler` **bỏ qua `RoleIds`**, luôn gán cứng `TenantAdmin`. Không có đường nào tạo được user role Employee |
| F4 | `GET /api/systemadmin/users` = "danh sách tất cả Users" (docs 01) | ❌ **Sai requirement** | Handler lọc `u.UserRoles.Any(r => r.Name == "TenantAdmin")` → **chỉ thấy TenantAdmin**. Đã test: search `user@acme.com` → trả rỗng |
| F5 | `GET /api/tenantadmin/storage-stats`, `GET /api/departmentadmin/stats` (docs 02/03) | ❌ **Thiếu** | Không tồn tại |
| F6 | Cây thư mục: tạo/đổi tên/xóa folder | ❌ **Thiếu** | Chỉ tạo 1 folder gốc trong `SetupDepartment`; không có FolderController; permission `folder.create/update/delete` vô dụng |
| F7 | Chia sẻ file (`FileShare` entity + quyền `file.share`) | ❌ **Thiếu** | Entity + bảng tồn tại, **không có endpoint** → entity chết |
| F8 | Refresh token (bảng `RefreshTokens`, entity, index đầy đủ) | ❌ **Thiếu** | Không cấp, không validate, không endpoint refresh → user hết token phải login lại |
| F9 | Route theo docs (`/api/tenantadmin/...`, `/api/departmentadmin/setup-department`) | ⚠️ **Sai lệch** | Thật: `/api/tenant/{tenantId}/...`, `/api/department/setup` → client viết theo docs sẽ 404 |
| F10 | `AcceptInvitation` tự tạo `UserStorageQuota` (docs 02 – Bước 3) | ⚠️ **Thiếu** | Handler thật **không** tạo quota (chỉ `CreateUser`/`CreateDepartmentAdmin` mới tạo) |
| F11 | `LastLoginAt` / `MarkLogin()` | ⚠️ **Dead code** | `MarkLogin()` không được gọi ở bất kỳ đâu → cột luôn NULL |
| F12 | `GET /api/user/{id}` | ⚠️ **Stub** | Trả `Ok()` rỗng, không `[Authorize]`, không dữ liệu |
| F13 | `POST /api/user/login` (trùng `AuthController.login`) | ⚠️ **Duplicate** | 2 endpoint login; bản trong `UserController` không validate, ném `InvalidOperationException` → 500 khi email trùng |
| F14 | Employee được "Download & Xóa File" (docs 04) | ⚠️ **Mâu thuẫn quyền** | Role `Employee` **không có** permission `file.delete` → nếu sau này gắn permission vào policy thì employee không xóa được file như docs mô tả |
| F15 | `TenantAdmin` seed kèm `DepartmentId = IT` | ⚠️ **Sai model** | docs nói TenantAdmin không thuộc phòng ban; seed lại gán vào phòng IT (và vô tình cho TenantAdmin quyền upload) |

### Bug logic cụ thể (đã truy vết code)

| # | Bug | Vị trí | Mức |
|---|---|---|---|
| B1 | **Xóa file KHÔNG hoàn `Department.StorageUsedBytes`** (chỉ hoàn provider/tenant/user) → quota phòng ban bị ăn mòn vĩnh viễn, không thể phục hồi | `DeleteFileCommandHandler` | 🔴 |
| B2 | **Check-then-act quota không transaction/lock** → 2 request upload đồng thời cùng vượt quota (race) | `UploadFileCommandHandler`, mọi `Allocate*` | 🟠 |
| B3 | **Upload nhiều file: fail giữa chừng → file trước đã lưu, quota đã cộng, trả 400** → dữ liệu nửa vời, không rollback | `FilesController.Upload` loop | 🟠 |
| B4 | **Quota overcommit khi tạo user**: mỗi user mới được `UserStorageQuota = toàn bộ Tenant.QuotaBytes` (10GB) → 10 user = 100GB quota trên tenant 10GB | `CreateUserCommandHandler` bước 4 | 🔴 |
| B5 | **`CreateDepartmentAdmin` ghi đè `department.StorageQuotaBytes = quotaToAllocate`** (không cộng dồn) và đồng thời cấp cho admin 1 ví bằng đúng số đó → double-book quota | `CreateDepartmentAdminCommandHandler` Bước 4 + 8 | 🔴 |
| B6 | **`AllocateUserStorageQuota` bỏ qua `departmentId` của route** → cấp quota chéo phòng ban/tenant (IDOR) | `DepartmentQuotasController` → command chỉ nhận `UserId` | 🔴 |
| B7 | **`AuditLogs.UserId = currentUser ?? Guid.Empty`** → FK `AuditLogs_Users_UserId` fail → **500** khi caller không có claim (đã test thật) | `AllocateUserStorageQuotaCommand`, `AllocateDepartmentStorageQuotaCommand` | 🟠 |
| B8 | **`DeleteUser` hard-delete** nhưng `FileEntries.OwnerId` FK `Restrict` → xóa user có file = **FK violation 500** | `DeleteUserCommandHandler` | 🟠 |
| B9 | **Token invitation dùng 1 lần không chống race**: 2 request song song cùng qua check `IsUsed` (không lock, không unique constraint_logic) | `AcceptInvitationCommandHandler` | 🟡 |
| B10 | **Không cập nhật `LastLoginAt`**, không revocation token khi `ChangeUserStatus(false)` / `ResetPassword` → user bị khóa vẫn dùng JWT cũ tới khi hết hạn (60'×) | `JwtTokenService`, `ChangeUserStatusCommandHandler` | 🟠 |
| B11 | **`GetTenantFiles`: client truyền `tenantId` bất kỳ** → đọc được file tenant khác; không filter theo phòng ban của Employee (docs 04 bảo employee thấy phòng của mình) | `GetTenantFilesQueryHandler` | 🔴 |
| B12 | **ClamAV fail-open + mặc định tắt** (`ClamAV:Enabled` không có trong appsettings → `_enabled = false` → always Clean) → quảng cáo "antivirus" nhưng thực tế không quét | `ClamAvVirusScannerService` | 🟠 |
| B13 | **Path traversal khi lưu file local**: `uniqueFileName = $"{Guid}_{fileName}"` với `fileName` do client gửi (`../../../evil`) | `LocalStorageService.UploadFileAsync` | 🔴 |
| B14 | **`CreateUser` (UserController) tạo user không có role nào** → user tồn tại nhưng không login/hoạt động được gì | `Application/Users/Commands/CreateUser` | 🟠 |
| B15 | `AuthController` trả `500 + ex.Message` (lộ nội bộ); `StorageProvidersController.Connect` trả `400 + ex.Message` | controller | 🟠 |

---

## 5. NHUNG VAN DE VE UX

*(Lưu ý: repo **không có code frontend** → đánh giá UX ở tầng API + tài liệu)*

| # | Vấn đề | Mức |
|---|---|---|
| U1 | **Không có refresh token** → sau 60 phút user bị văng, không có silent refresh; UX bắt buộc login lại | 🟠 |
| U2 | **Không có rate limit / lockout đăng nhập** → brute force password thoải mái; không có captcha | 🔴 |
| U3 | **Thông báo lỗi 500 "mù"** cho hầu hết lỗi (validation của login, FK, schema) → người dùng không biết phải làm gì | 🟠 |
| U4 | **Docs ≠ API thật** (mục 4, F9) → devs/op làm theo tài liệu sẽ 404, không có OpenAPI/Postman collection thay thế ngoài Swagger (Swagger chỉ bật ở Development) | 🟠 |
| U5 | **Gửi email thất bại bị nuốt** (`catch {}` trong `EmailService`) → admin thấy "201 Created" nhưng người dùng không nhận được mail, không có retry/outbox, không báo lại UI | 🟠 |
| U6 | `GET /api/files` **không phân trang** → hàng nghìn file trả 1 payload; không sort/filter/search | 🟠 |
| U7 | Trạng thái **disabled/loading không được hỗ trợ bởi API**: không có idempotency key → double-click "Upload"/"Create user" tạo trùng (email có unique nên fail 500 chứ không phải message đẹp) | 🟡 |
| U8 | Thông báo lỗi đồng nhất bằng `Result{errors:[...]}` nhưng một số chỗ trả `{message}` → client phải parse 2 dạng | 🟡 |
| U9 | Không có endpoint "me/profile" → UI phải tự decode JWT | 🟢 |
| U10 | Login không trả refresh token, không cập nhật `lastLoginAt` → UI không biết phiên cũ | 🟢 |

---

## 6. NHUNG VAN DE VE SECURITY

*(Đánh giá như Security Engineer — mức độ dựa trên code + test thật)*

| # | Kiểm mục | Kết luận | Mức |
|---|---|---|---|
| S1 | **Authentication** | Gần như toàn bộ endpoint **anonymous** (C1, đã test) | 🔴 |
| S2 | **Authorization / RBAC** | `PermissionPolicyProvider` tự tạo policy cho *mọi* tên policy nhưng **chỉ 1 endpoint dùng** → RBAC là bình phong | 🔴 |
| S3 | **Broken Access Control / IDOR** | download/delete file, quota, admin user API đều không check chủ sở hữu/tenant (C3, B6, B11) | 🔴 |
| S4 | **Privilege Escalation** | `POST /api/admin/users/{id}/roles` đặt role **bất kỳ** (kể cả SystemAdmin) không auth; `AssignUserRoles` cho phép tự nâng quyền | 🔴 |
| S5 | **Impersonation** | `userId/tenantId/storageProviderId` từ client + fallback "user đầu tiên" (C2) | 🔴 |
| S6 | **File Upload** | Không giới hạn dung lượng (`MaxRequestBodySize` mặc định 30MB nhưng không có policy per-file), không whitelist contentType/ext, **path traversal** (B13), không kiểm tra file trùng hash | 🔴 |
| S7 | **Virus Scan** | Tắt mặc định + fail-open (B12) | 🟠 |
| S8 | **JWT** | Secret hard-code fallback + commit repo; không `jti`, không revocation, không refresh; payload chứa toàn permission (fine) nhưng **role đổi không hiệu lực tới khi token hết hạn** | 🔴 |
| S9 | **Password** | BCrypt OK; nhưng **không có chính sách độ phức tạp** (chỉ `min 6`), không có forgot/reset-password qua email (admin đặt mật khẩu trực tiếp — B: `ResetUserPassword` ghi thẳng), không force đổi pass | 🟠 |
| S10 | **Brute force / Rate limit** | Không có (không `AddRateLimiter` ở đâu) | 🔴 |
| S11 | **Secret/Sensitive Data** | App password Gmail + JWT key + DB root trong git (C6); `ExchangeOneDriveCode` **trả `RefreshToken`, `AccessToken`, `ConfigJson` (chứa `client_secret`) về client** | 🔴 |
| S12 | **Token invitation** | Lưu plaintext (không hash) trong DB; log **link kích hoạt chứa token** ở mức Information → token leak qua log; `GET validate-invitation` cho phép dò token+email | 🟠 |
| S13 | **XSS / Output Encoding** | Backend trả JSON (an toàn), **nhưng email HTML chèn thẳng `recipientName`/`tenantName`/`departmentName` không escape** → HTML injection trong email | 🟡 |
| S14 | **CSRF** | Dùng Bearer token (không cookie) → rủi ro CSRF thấp, **nhưng CORS `AllowAnyOrigin` + `AllowAnyHeader/Method`** vô hiệu hóa cấu hình `Cors:AllowedOrigins` trong appsettings (config chết) | 🟡 |
| S15 | **SQL Injection** | Dùng EF Core parameterized → **an toàn** (không phát hiện raw SQL nào ngoài ALTER/CREATE trong Program.cs — những cái đó là hằng số, không injection) | ✅ |
| S16 | **Multi-tenant isolation** | Không có global query filter theo `TenantId` (không `IQueryFilter`), từng handler tự nhớ filter → rò rỉ cross-tenant (B11) | 🔴 |
| S17 | **Audit & logging** | Có audit nhưng không có log đăng nhập thất bại, không có correlation id (`LoggingBehavior` là stub, không được đăng ký) | 🟡 |
| S18 | **Demo accounts** | Seed `admin@clouddms.com/Admin@123` + 3 account nữa **luôn chạy kể cả Production** | 🟠 |

---

## 7. NHUNG VAN DE VE PERFORMANCE

| # | Vấn đề | Bằng chứng | Mức |
|---|---|---|---|
| P1 | `GET /api/files` **không phân trang** → `ToListAsync()` toàn bộ file của tenant | `GetTenantFilesQueryHandler` | 🟠 |
| P2 | `GET /api/admin/users` **không giới hạn `PageSize`** (`pageSize=1000000` hợp lệ) + `Include` 4 cấp (User→UserRole→Role→RolePermission→Permission) rồi mới `Skip/Take` | `GetUsersWithPaginationQueryHandler` | 🟠 |
| P3 | `GetDepartments` load **mọi user + roles** của phòng chỉ để đếm `HeadCount`/`Lead` → cartesian join, EF cảnh báo `MultipleCollectionIncludeWarning` (đã thấy trong log thật) | `GetDepartmentsQueryHandler` + log | 🟠 |
| P4 | Search dùng `.ToLower().Contains()` → **không sargable**, full scan trên Users | same | 🟡 |
| P5 | **Không có cache** nào (memory/distributed) cho role/permission/tenant quota → mỗi login 2 query, mỗi request policy check đọc claim (OK) nhưng quota đọc DB mỗi upload | — | 🟡 |
| P6 | Seed RBAC: `AnyAsync` trong vòng lặp từng permission (N+1 ở startup) | `DbInitializer` | 🟢 |
| P7 | **Không giới hạn dung lượng upload**; lưu file local bằng buffer 4KB; không có streaming policy → file lớn chiếm RAM/đĩa | `LocalStorageService` | 🟠 |
| P8 | Upload **không có transaction**: cộng 4 bảng (userQuota/department/tenant/provider) trong 1 `SaveChanges` (OK) nhưng **check quota ở trước đó hàng trăm ms** → race (B2) | — | 🟠 |
| P9 | `EnableRetryOnFailure(5, 10s)` + thao tác không idempotent (upload) → retry có thể nhân bản ghi | `DependencyInjection.cs` | 🟡 |
| P10 | **Không có index** cho truy vấn nóng `FileEntries(TenantId, IsDeleted, CreatedAt)` (chỉ có index rời rạc 4 cột) | `FileEntryConfiguration` | 🟡 |
| P11 | Không có Docker/CDN/compression config; Swagger chỉ Dev; không cấu hình response caching | — | 🟢 |

---

## 8. NHUNG VAN DE VE DATABASE

| # | Vấn đề | Chi tiết | Mức |
|---|---|---|---|
| D1 | **Schema lệch thật sự** — đã chứng minh bằng request thật (`Unknown column f.DepartmentId`) | Nguyên nhân: trộn migration + ALTER raw + `catch{}` (C5) | 🔴 |
| D2 | **Thừa cấu hình trùng lặp**: tồn tại **2 folder** `Persistence/Configuations/` (đ typo) và `Persistence/Configurations/` với **9 class cấu hình trùng cho cùng entity** (User, Tenant, Role, Department, Permission, RefreshToken, AuditLog, UserRole, RolePermission). `ApplyConfigurationsFromAssembly` áp **cả 2** → thứ tự assembly quyết định cấu hình nào thắng, sửa 1 bên mà quên bên kia = model thay đổi âm thầm | `AppDbContext.OnModelCreating` | 🔴 |
| D3 | **Thừa bảng/cột/entity chết**: `Products` (DbSet + entity + DTO, không dùng), `FileShares` (không endpoint), `RefreshTokens` (không flow), cột `Users.LastLoginAt` (không ai ghi), `User.IsActive` trùng khái niệm với `EmailConfirmed` | — | 🟠 |
| D4 | **Thiếu index**: `FileEntries` không có composite `(TenantId, IsDeleted, CreatedAt)`; `AuditLogs` không index `TenantId/CreatedAt` (truy vấn audit tới đây sẽ chậm); `Departments` không unique `(TenantId, Code)` ở DB → race tạo 2 phòng trùng code (code chỉ check `AnyAsync`) | configs | 🟠 |
| D5 | **Quota là số liệu denormalized 4 chỗ** (provider/tenant/dept/user) cập nhật bằng `+=` không lock → lệch số là chuyện đương nhiên khi có concurrent, không có job đối soát | — | 🔴 |
| D6 | **Delete user hard-delete** vs FK `Restrict` ở FileEntries/AuditLogs → không xóa được (B8). Thiếu chiến lược xóa/ẩn danh user | — | 🟠 |
| D7 | **Cascade không nhất quán**: `UserStorageQuota` Cascade, `FileEntry.Owner` Restrict, `EmailVerificationToken` Cascade… không có doc dữ liệu về hành vi xóa | configs | 🟡 |
| D8 | Migration **chỉ có 3 cái** (Initial, AddRBAC, AddEmailVerificationTokensAndQuota) nhưng code có nhiều bảng/cột hơn (StorageProviders, ScanStatus, DepartmentId…) → migration history đã bị vượt qua bởi code | `Migrations/` | 🔴 |
| D9 | Lưu `StorageKey` unique index OK, nhưng `Name` file không lưu tên đã sanitize → hiển thị lại có thể chứa ký tự điều khiển | — | 🟢 |
| D10 | `char(36)` GUID + MySQL OK; naming chuẩn PascalCase bảng, `IX_` index OK | — | ✅ |

---

## 9. NHUNG VAN DE VE ARCHITECTURE

| # | Vấn đề | Chi tiết |
|---|---|---|
| A1 | **2 bộ CQRS song song** cho cùng use-case: `Features/SystemAdmin/CreateUser` (Result<UserDto>) vs `Users/Commands/CreateUser` (Guid) — giống hệt với UpdateUser, Login (2 endpoint) | Vi phạm DRY, gây nhầm lẫn nghiêm trọng khi sửa |
| A2 | **9 file cấu hình EF trùng** trong 2 folder `Configuations`/`Configurations` (D2) | Vi phạm SRP/DRY, rủi ro model thay đổi âm thầm |
| A3 | **Stub/template code còn nguyên** trong production: `LoggingBehavior` (không đăng ký, không log), `GlobalExceptionHandlerMiddleware` (không đăng ký), `Product`/`ProductDto`, README Patreon | Code "giả" tồn tại gây hiểu nhầm |
| A4 | **Hai extension trùng tên `AddInfrastructureServices`** (một nhận `IConfiguration`, một nhận `string?` với chuỗi hard-code `root/root`) — hàm thứ hai chết nhưng là mìn | `DependencyInjection.cs` vs `InfrastructureServiceExtensions.cs` |
| A5 | **Handler tự làm quá nhiều việc**: `UploadFileCommandHandler` ~300 dòng (identity + quota + scan + hash + storage + audit + format bytes) → khó test, vi phạm SRP | — |
| A6 | **`FormatBytes` copy-paste ở 5+ file**; magic numbers `5GB/10GB/1GB/15GB` hard-code rải rác (một chỗ `5L*1024^3` trong entity, chỗ khác `10L*1024^3`) | DRY/KISS vi phạm |
| A7 | **Layering bị rò rỉ**: `IApplicationDbContext`暴露 `Set<>`-style DbSet → handler EF tùy ý; một số handler (`AllocateTenantStorageQuota`) **throw exception thay vì Result** → controller phải try/catch → không nhất quán | — |
| A8 | **Không có domain service/domain event**: toàn bộ business rule nằm trong handler (anemic domain model) — acceptable với CQRS nhưng quota rule lẽ ra là domain service dùng chung | — |
| A9 | Folder `Common/Interfaces/Repositorires/` **đ typo** | — |
| A10 | Không có tầng `Presentation`/`Shared`, `API` chứa JWT config, CORS, seeding logic 150 dòng trong `Program.Main` → `Main` quá dài, khó bảo trì | — |
| A11 | **Không có test project nào** → không có safety net khi refactory | 🔴 |

**Nên tách (được hỏi ở mục 3):**
- `QuotaCalculator` / `StorageQuotaService` (dùng chung cho 5 handler cấp quota hiện đang lặp lại logic 3 lớp).
- `CurrentUserAuthorizationGuard` (check tenant/department scope) — hiện mỗi handler tự viết, cái có cái không.
- `FileSanitizer` (tên file, path), `ByteFormatter` (thay `FormatBytes` copy-paste).
- `InvitationService` (tạo token + gửi mail + ghi audit) — hiện lặp ở 2 handler.
- Tách `Program.cs` thành `MigrationExtensions` / `SwaggerExtensions` / `CorsExtensions`.

---

## 10. DANH SACH BUG

**Độ tin cậy:** ✅ = đã kiểm chứng bằng request thật/build; 🟡 = đã truy vết code kỹ (chưa test runtime); ⚪ = cần test thêm.

| ID | Bug | Mức | Tin cậy |
|---|---|---|---|
| BUG-01 | Không có `[Authorize]`/FallbackPolicy → toàn bộ API anonymous | 🔴 Critical | ✅ |
| BUG-02 | Upload fallback "user đầu tiên trong DB" + `userId/tenantId` từ client | 🔴 Critical | ✅ |
| BUG-03 | Download/Delete file không check tenant/owner (IDOR) | 🔴 Critical | 🟡 |
| BUG-04 | `GET /api/files?tenantId=` cho phép đọc file tenant khác, không scope phòng ban | 🔴 Critical | 🟡 |
| BUG-05 | `AllocateUserStorageQuota` bỏ qua `departmentId` route (cấp quota chéo phòng) | 🔴 Critical | 🟡 |
| BUG-06 | Đổi mật khẩu/Reset user không auth (đã thấy handler chạy ẩn danh) | 🔴 Critical | ✅ |
| BUG-07 | Secret (Gmail app password, JWT key, DB) commit vào git | 🔴 Critical | ✅ |
| BUG-08 | Migration raw SQL + nuốt lỗi → DB lệch → 500 toàn bộ module File | 🔴 Critical | ✅ |
| BUG-09 | Xóa file không hoàn `Department.StorageUsedBytes` | 🔴 High | 🟡 |
| BUG-10 | `CreateUser` cấp quota = toàn bộ quota tenant cho mỗi user (overcommit) | 🔴 High | 🟡 |
| BUG-11 | `CreateDepartmentAdmin` ghi đè quota phòng + double-book ví admin | 🔴 High | 🟡 |
| BUG-12 | `RoleIds` bị bỏ qua, luôn gán TenantAdmin → không tạo được Employee | 🔴 High | 🟡 |
| BUG-13 | Path traversal trong `LocalStorageService` (tên file client) | 🔴 High | 🟡 |
| BUG-14 | `ExchangeOneDriveCode` trả `client_secret`/`refresh_token`/`access_token` về client | 🔴 High | 🟡 |
| BUG-15 | 9 file cấu hình EF trùng trong 2 folder (model ưu tiên không xác định) | 🔴 High | ✅ (đã đếm) |
| BUG-16 | `GlobalExceptionHandlerMiddleware` không đăng ký; `AuthController` trả `ex.Message` | 🟠 High | ✅ |
| BUG-17 | Không có rate limit/brute force protection | 🟠 High | ✅ |
| BUG-18 | Không có refresh token flow (bảng có sẵn, code không có) | 🟠 High | ✅ |
| BUG-19 | Không revoke JWT khi deactivate/reset password | 🟠 High | 🟡 |
| BUG-20 | Race condition check-then-write quota (upload + mọi Allocate) | 🟠 High | 🟡 |
| BUG-21 | Upload nhiều file fail giữa chừng → dữ liệu nửa vời, không rollback | 🟠 High | 🟡 |
| BUG-22 | `DeleteUser` hard-delete vs FK Restrict → 500 khi user có file | 🟠 High | 🟡 |
| BUG-23 | `AuditLog.UserId = Guid.Empty` → FK fail 500 | 🟠 High | ✅ |
| BUG-24 | ClamAV tắt mặc định + fail-open | 🟠 High | 🟡 |
| BUG-25 | Seed tài khoản admin mật khẩu yếu chạy mọi môi trường | 🟠 High | ✅ |
| BUG-26 | `GetUsersWithPagination` chỉ trả TenantAdmin (sai requirement docs) | 🟠 Medium | ✅ |
| BUG-27 | `GET /api/files` không phân trang | 🟠 Medium | 🟡 |
| BUG-28 | `PageSize` không giới hạn trên | 🟠 Medium | 🟡 |
| BUG-29 | Email gửi fail bị nuốt, không retry, không báo UI | 🟠 Medium | 🟡 |
| BUG-30 | Token invitation lưu plaintext + log link chứa token | 🟠 Medium | 🟡 |
| BUG-31 | HTML injection trong email (tên chèn thẳng) | 🟡 Medium | 🟡 |
| BUG-32 | `LastLoginAt`/`MarkLogin` dead code | 🟡 Medium | ✅ |
| BUG-33 | `LoggingBehavior` không được đăng ký (dead code) | 🟡 Medium | ✅ |
| BUG-34 | `AcceptInvitation` không tạo `UserStorageQuota` (sai docs) | 🟡 Medium | 🟡 |
| BUG-35 | Race accept-invitation 2 lần cùng lúc (không lock token) | 🟡 Medium | ⚪ |
| BUG-36 | Search `.ToLower().Contains` không sargable | 🟡 Low | 🟡 |
| BUG-37 | `AddInfrastructureServices(string?)` hard-code `root/root` (dead nhưng nguy hiểm) | 🟡 Low | ✅ |
| BUG-38 | CORS config trong appsettings bị bỏ qua (AllowAnyOrigin) | 🟡 Low | ✅ |
| BUG-39 | Duplicate endpoint login + `UserController.GetById` stub rỗng | 🟢 Low | ✅ |
| BUG-40 | README sai hoàn toàn (.NET 10, Product API, Patreon) | 🟢 Low | ✅ |

---

## 11. DANH SACH CAI TIEN

| Mức độ | Vấn đề | Ảnh hưởng | Cách sửa |
|---|---|---|---|
| 🔴 | Thiếu `[Authorize]`/FallbackPolicy toàn cục | Toàn bộ hệ thống bị hack được chỉ với curl | `FallbackPolicy = RequireAuthenticatedUser` + `[Authorize(Policy=...)]` từng endpoint + test 401/403 |
| 🔴 | IDOR download/delete file, quota, admin API | Lộ/thay đổi dữ liệu mọi tenant | Guard theo `tenantId` từ JWT trong mọi handler; dùng `IQueryFilter` EF cho `TenantId` |
| 🔴 | Upload cho phép `userId/tenantId` override + fallback | Ghi ẩn danh, phá quota người khác | Xóa override + fallback, chỉ dùng JWT |
| 🔴 | Migration hỗn loạn, DB lệch thật | Module File sập 500 | Bỏ ALTER raw, dùng EF Migration đúng nguồn, thêm bước verify schema khi start |
| 🔴 | Secret trong git | Cướp tài khoản email/JWT | Đổi app password, đưa sang env/user-secrets, quét secret |
| 🔴 | Path traversal + không giới hạn upload | Đọc/ghi file ngoài thư mục, DoS | Sanitize tên file (chỉ `[a-zA-Z0-9._-]`), `RequestSizeLimit`, whitelist extension/content-type |
| 🔴 | Quota overcommit (B4, B5, B11) + race | Số liệu sai, user ghi đè quota nhau | Tách `QuotaService` + transaction `BEGIN` + `SELECT ... FOR UPDATE` (hoặc乐观 concurrency token `rowversion`) |
| 🟠 | Không có global exception handler | Lộ internals, 500 mù | Đăng ký middleware/`AddProblemDetails`, map lỗi chuẩn |
| 🟠 | Không rate limit | Brute force | `AddRateLimiter` theo IP/email cho login + upload |
| 🟠 | Không refresh token + không revoke | Token ăn cắp dùng mãi | Cấp refresh token xoay vòng, blacklist `jti` khi deactivate/reset pass |
| 🟠 | Xóa file không hoàn quota phòng ban | Quota phòng bị ăn mòn | Trong `DeleteFile`: load department + `StorageUsedBytes -= size` (trong transaction) |
| 🟠 | 2 bộ code trùng (Users/ vs Features/) | Sửa bên này, hỏng bên kia | Giữ 1, xóa stub, merge `UserController` vào `AuthController` |
| 🟠 | 9 cấu hình EF trùng 2 folder | Model thay đổi âm thầm | Xóa folder `Configuations` (đ typo), merge về `Configurations`, thêm bước CI kiểm tra duplicate |
| 🟠 | Thiếu chức năng theo docs (audit-log, folder CRUD, employees, storage-stats, FileShare) | Feature incomplete | Lập bảng endpoint对照 docs, hoặc cập nhật docs cho khớp |
| 🟠 | ClamAV mặc định tắt/fail-open | Malware lọt | Bật mặc định, fail-closed ở Production, thêm config |
| 🟠 | Email fail bị nuốt | User không nhận mail, support khó debug | Outbox pattern + trả warning trong response |
| 🟡 | Không phân trang files/departments, Include nặng | Chậm khi 10k+ file | Thêm `PagedResult`, `AsSplitQuery`, index `(TenantId, IsDeleted, CreatedAt)` |
| 🟡 | `PageSize` không cap | DoS | `Math.Min(pageSize, 100)` |
| 🟡 | Không có test/CI | Regression cao | Thêm xUnit + WebApplicationFactory test 401/403/quota/race; GitHub Actions |
| 🟡 | Email HTML không escape; log token | Injection / leak token | `HtmlEncoder.Default.Encode`, không log link |
| 🟡 | `ex.Message` trả client | Lộ thông tin nội bộ | Chỉ trả generic message, log chi tiết server-side |
| 🟢 | README sai, typo `Repositorires`/`Configuations`, magic number | Maintainability | Cập nhật README theo code thật, hằng số vào `QuotaDefaults` |
| 🟢 | Dead code (`Product`, `LoggingBehavior`, `RefreshToken`, `FileShare`, `MarkLogin`) | Nhầm lẫn | Xóa hoặc hoàn thiện |

---

## 12. CHECKLIST CON THIEU

**Thiếu hoàn toàn trong repo (đã kiểm tra bằng glob/grep):**

- [ ] **Unit tests / Integration tests** — 0 file test (`detected_test_files: 0`)
- [ ] **CI/CD** — không có `.github/workflows`
- [ ] **Docker / docker-compose** — không có (mặc dù mục review yêu cầu)
- [ ] **Frontend** — không tồn tại trong repo → không review được React/Vue, useEffect, bundle, lazy loading… (điểm Frontend = N/A)
- [ ] **Rate limiting / API throttling**
- [ ] **Refresh token flow**
- [ ] **Swagger ở môi trường Production** (chỉ Development) → không có API doc cho môi trường chung
- [ ] **OpenAPI/Postman collection** thay docs đã lỗi
- [ ] **HTTPS redirection** (`UseHttpsRedirection` không có)
- [ ] **HSTS / Security Headers** (X-Content-Type-Options, CSP…) — không có
- [ ] **CORS động theo origin** (hiện AllowAnyOrigin)
- [ ] **Structured logging / correlation id / APM** (LoggingBehavior stub)
- [ ] **Health check** (`AddHealthChecks` không có)
- [ ] **Background job dọn file rác / đối soát quota**
- [ ] **Xóa file vật lý khi soft-delete** (hiện file local/cloud vẫn nằm đó, quota thì đã hoàn → hao hụt storage vật lý)
- [ ] **Audit log cho login/logout/failed login**
- [ ] **Chính sách mật khẩu** (độ phức tạp, lịch sử, lockout)
- [ ] **Xóa/quy ẩn dữ liệu user** (GDPR-ish) thay vì hard-delete
- [ ] **Capacity test** (100 → 100k users) — chưa có benchmark nào

**Cần xem thêm để chốt các phát hiện chưa chắc chắn (giảm độ tin cậy 🟡/⚪):**
- Kết quả runtime của race condition (cần test song song 2 luồng upload) — BUG-35, BUG-20
- File cấu hình nào "thắng" giữa `Configuations` vs `Configurations` (cần in `IModel` khi runtime) — BUG-15
- `GoogleDriveStorageService`/`OneDriveStorageService` (đã thấy warning obsolete, chưa đọc hết luồng quota cloud thực tế)
- Hành vi thực của `PermissionPolicyProvider` khi có policy lạ (mọi tên policy đều được tạo)

---

## 13. DIEM TONG THE

| Tiêu chí | Điểm (0–10) | Nhận xét |
|---|---|---|
| Logic | **4** | Quota nhiều lớp là điểm cộng, nhưng overcommit (B4/B5), hoàn quota thiếu phòng ban (B1), race, fallback user mập mờ |
| UI | **0** | Không tồn tại frontend trong repo |
| UX | **4** | Thông báo lỗi tiếng Việt tốt, nhưng 500 mù, không refresh token, docs sai, không phân trang |
| Performance | **5** | Query EF hợp lý ở mức nhỏ, nhưng không phân trang, Include nặng, không cache, không giới hạn upload |
| Security | **1** | Gần như toàn bộ API anonymous (đã test), IDOR, secret trong git, không rate limit, path traversal |
| Architecture | **5** | Clean Architecture đúng khung, nhưng 2 bộ code trùng, 9 config trùng, stub chưa đăng ký, Main 300 dòng |
| Database | **4** | Thiết kế bảng/relationship đạt, có index unique, nhưng schema thật đang lệch, thiếu composite index, quota denormalized 4 chỗ không đồng bộ |
| Frontend | **0** | Không có code frontend để review |
| Backend | **4** | CQRS/Result/FluentValidation chạy đúng, nhưng exception handling, auth, transaction đều thiếu |
| Maintainability | **5** | Naming khá tốt, có comment tiếng Việt, nhưng duplication + typo folder + dead code |
| Scalability | **4** | Mở rộng tới ~1k user sẽ bắt đầu nghẽn (phân trang, quota race, DB đơn, không cache); 100k cần thiết kế lại |
| Code Quality | **4** | Không test, magic number, copy-paste `FormatBytes`, mixed ngôn ngữ/style |

**ĐIỂM TỔNG TRUNG BÌNH: 3.7 / 10**

*Mức: 0–2 = không thể triển khai · 3–4 = demo nội bộ có kiểm soát · 5–6 = beta cần sửa · 7–8 = production được · 9–10 = xuất sắc*

> **Kết luận:** Kiến trúc khung là đạt (điểm cộng thật sự), nhưng **hệ thống hiện tại KHÔNG AN TOÀN để triển khai** — một request curl không token đã đọc được toàn bộ danh sách user kèm PII trong môi trường test. Ưu tiên duy nhất hợp lý ở giai đoạn này là khoá API lại trước, rồi mới thêm chức năng.

---

## 14. KE HOACH UU TIEN SUA LOI (Critical → Low)

### 🔴 Phase 1 — "Dừng rò rỉ" (làm ngay, 1–2 ngày)
1. **Thêm xác thực toàn cục**: FallbackPolicy `RequireAuthenticatedUser` + `[Authorize]` trên `ApiControllerBase`; bật lại Swagger Production để test nhanh. *(BUG-01)*
2. **Gắn policy quyền theo endpoint** (`file.upload`, `user.create`, `storage.manage`…) — hệ thống permission đã seed sẵn, chỉ thiếu nơi gắn. *(BUG-01, BUG-04)*
3. **Xóa nhánh impersonation trong upload**: bỏ `UserIdOverride/TenantIdOverride/StorageProviderIdOverride` + 2 fallback. *(BUG-02)*
4. **Chốt scope tenant**: guard `fileEntry.TenantId == currentUser.TenantId` ở download/delete/list; bỏ tham số `tenantId` ở query. *(BUG-03, BUG-04)*
5. **Đổi các secret đã lộ**: app password Gmail, JWT key, DB user; chuyển sang env/user-secrets; thêm `.gitignore` + quét secret. *(BUG-07)*
6. **Sanitize tên file + giới hạn dung lượng upload** (path traversal). *(BUG-13)*

### 🟠 Phase 2 — "Chức năng đúng" (tuần 1)
7. **Sửa migration**: bỏ toàn bộ ALTER/CREATE raw trong `Program.cs`, đồng bộ EF Migration với DB thật, thêm bước kiểm tra schema khi boot. *(BUG-08)*
8. **Sửa logic quota**: hoàn `Department.StorageUsedBytes` khi xóa file; bỏ overcommit khi tạo user; sửa double-book ở `CreateDepartmentAdmin`. *(BUG-09, 10, 11)*
9. **Transaction + concurrency token** cho mọi thao tác cộng/trừ quota. *(BUG-20)*
10. **Đăng ký global exception handler**, ngừng trả `ex.Message`. *(BUG-16)*
11. **Gán đúng role khi tạo user** (dùng `RoleIds`); bổ sung luồng tạo Employee. *(BUG-12)*
12. **Merge 2 bộ code trùng** (`Users/` vs `Features/`), xóa 1 trong 2 `AddInfrastructureServices`, xóa folder `Configuations`. *(BUG-15)*

### 🟡 Phase 3 — "Vận hành được" (tuần 2–3)
13. Rate limit login/upload + lockout + audit login thất bại.
14. Refresh token + revoke khi deactivate/reset pass.
15. Phân trang `GET /api/files`, cap `PageSize`, `AsSplitQuery`, composite index `(TenantId, IsDeleted, CreatedAt)`.
16. Cập nhật `docs/` cho khớp route thật hoặc sinh OpenAPI từ code.
17. Bật ClamAV fail-closed, outbox email, không log token.
18. Bổ sung chức năng còn thiếu: folder CRUD, audit-log API, storage-stats, FileShare (hoặc xóa entity).

### 🟢 Phase 4 — "Chất lượng dài hạn" (tuần 4+)
19. Thêm test project (ít nhất: auth 401/403, quota boundary, upload, permission) + CI chạy `dotnet test` ở mọi PR.
20. Dockerfile + docker-compose (app + mysql + clamav) + health check.
21. Dọn dead code (`Product`, `LoggingBehavior` stub hoặc đăng ký nó, `RefreshToken`/`FileShare`), sửa typo, đưa magic number vào hằng số.
22. Viết lại README đúng với dự án thật.
23. Benchmark ở 100 / 1k / 10k user trước khi cam kết khả năng mở rộng.

---

*Phương pháp review: đọc toàn bộ 4 layer, đối chiếu `docs/` với code, build Release, chạy server thật và pen-test các endpoint (chi tiết tại `01_TEST_EVIDENCE.md`). Các kết luận đều ghi rõ mức độ tin cậy; mục nào chưa kiểm chứng runtime được đã đánh dấu 🟡/⚪ ở mục 10 và 12.*
