# BANG THEO DOI — 40 BUG → TASK

> Nguồn bug: `review/00_REVIEW_TOAN_BO.md` mục 10.
> Cập nhật cột **Trạng thái** khi làm: `TODO` → `WIP` → `DONE` → `VERIFIED` (đã chạy `06_CHECKLIST_KIEM_NGHIEM.md`).

**Trạng thái:** TODO · WIP · DONE · VERIFIED · N/A (quyết định không làm, phải ghi lý do)

---

## Phase 1 — 🔴 Critical

| Bug | Mô tả ngắn | Mức | Task | Files chính | Trạng thái |
|---|---|---|---|---|---|
| BUG-01 | Toàn bộ API anonymous (không `[Authorize]`/FallbackPolicy) | 🔴 | P1-01, P1-02 | `Program.cs`, `ApiControllerBase.cs`, mọi controller | VERIFIED |
| BUG-02 | Upload fallback "user đầu tiên CSDL" + `userId/tenantId` từ client | 🔴 | P1-03 | `UploadFileCommand(.cs)`, `UploadFileCommandHandler.cs`, `FilesController.cs` | VERIFIED |
| BUG-03 | Download/Delete file không check tenant (IDOR) | 🔴 | P1-04 | `DownloadFileQuery.cs`, `DeleteFileCommand.cs` | VERIFIED |
| BUG-04 | `GET /api/files?tenantId=` đọc file tenant khác | 🔴 | P1-04 | `GetTenantFilesQueryHandler.cs` | VERIFIED |
| BUG-05 | `AllocateUserStorageQuota` bỏ qua `departmentId` route | 🔴 | P1-05 | `DepartmentQuotasController.cs`, `AllocateUserStorageQuotaCommand.cs` | VERIFIED |
| BUG-06 | Reset-password / admin user API không auth | 🔴 | P1-01, P1-02 | `Admin/UsersController.cs` | VERIFIED |
| BUG-07 | Secret trong git (Gmail app pw, JWT key, DB root) | 🔴 | P1-06 | `appsettings.json`, `Program.cs`, `AesEncryptionService.cs`, `InfrastructureServiceExtensions.cs` | DONE (.env.example & gitleaks CI) |
| BUG-14 | `ExchangeOneDriveCode` trả `client_secret`/`refresh_token`/`access_token` về client | 🔴 | P1-06 | `ExchangeOneDriveCodeCommand.cs` | VERIFIED |
| BUG-13 | Path traversal tên file + không giới hạn upload | 🔴 | P1-07 | `LocalStorageService.cs`, `FilesController.cs` | VERIFIED |
| BUG-16 | Exception handler không đăng ký; trả `ex.Message` | 🔴 | P1-08 | `Program.cs`, `GlobalExceptionHandlerMiddleware.cs`, `AuthController.cs` | VERIFIED |

---

## Phase 2 — 🟠 Logic / DB

| Bug | Mô tả ngắn | Mức | Task | Files chính | Trạng thái |
|---|---|---|---|---|---|
| BUG-08 | Migration raw SQL + nuốt lỗi → DB lệch → 500 module File | 🔴 | P2-01 | `Program.cs`, `Migrations/` | VERIFIED |
| BUG-15 | 9 cấu hình EF trùng trong 2 folder (`Configuations`/`Configurations`) | 🔴 | P2-02 | `Persistence/Configuations/*` | VERIFIED |
| BUG-09 | Xóa file không hoàn `Department.StorageUsedBytes` | 🔴 | P2-03 | `DeleteFileCommand.cs` | VERIFIED |
| BUG-10 | Tạo user được cấp quota = toàn bộ quota tenant (overcommit) | 🔴 | P2-04 | `CreateUserCommandHandler.cs` (SystemAdmin) | VERIFIED |
| BUG-11 | `CreateDepartmentAdmin` ghi đè quota phòng + double-book ví admin | 🔴 | P2-05 | `CreateDepartmentAdminCommandHandler.cs` | VERIFIED |
| BUG-20 | Race condition check-then-write quota | 🟠 | P2-06 | mọi `Allocate*`, `Upload/DeleteFileCommand` | VERIFIED |
| BUG-35 | Race accept-invitation 2 lần (không lock token) | 🟡 | P2-06 | `AcceptInvitationCommand.cs` | VERIFIED |
| BUG-12 | `RoleIds` bị bỏ qua → luôn gán TenantAdmin, không tạo được Employee | 🔴 | P2-07 | `CreateUserCommandHandler.cs` | VERIFIED |
| BUG-26 | `GetUsers` chỉ trả về TenantAdmin (sai docs "tất cả Users") | 🟠 | P2-07 | `GetUsersWithPaginationQueryHandler.cs` | VERIFIED |
| — | Thiếu endpoint tạo Employee (docs 03) | ❌ | P2-07 | mới: `DepartmentEmployeesController` | VERIFIED |
| BUG-23 | `AuditLog.UserId = Guid.Empty` → FK fail 500 | 🟠 | P2-08 | `AllocateUserStorageQuotaCommand.cs`, `AllocateDepartmentStorageQuotaCommand.cs` | VERIFIED |
| BUG-22 | `DeleteUser` hard-delete vs FK Restrict → 500 khi user có file | 🟠 | P2-08 | `DeleteUserCommandHandler.cs` | VERIFIED |
| BUG-21 | Upload nhiều file fail giữa chừng → trạng thái nửa vời | 🟠 | P2-09 | `FilesController.cs` | VERIFIED |
| BUG-39 | Duplicate endpoint login + `UserController.GetById` stub rỗng | 🟢 | P2-10 | `UserController.cs` | VERIFIED |
| BUG-34 | `AcceptInvitation` không tạo `UserStorageQuota` (sai docs) | 🟡 | P2-10 | `AcceptInvitationCommand.cs` | VERIFIED |
| BUG-15b | 2 bộ code trùng `Users/` vs `Features/` + 2 `AddInfrastructureServices` | 🔴 | P2-10 | `Application/Users/*`, `InfrastructureServiceExtensions.cs` | VERIFIED |

---

## Phase 3 — 🟡 Vận hành

| Bug | Mô tả ngắn | Mức | Task | Files chính | Trạng thái |
|---|---|---|---|---|---|
| BUG-17 | Không rate limit / brute force | 🟠 | P3-01 | `Program.cs`, `AuthController.cs` | VERIFIED |
| BUG-18 | Không có refresh token flow (bảng có sẵn, code không) | 🟠 | P3-02 | `LoginCommandHandler.cs`, `AuthController.cs`, `JwtTokenService.cs` | VERIFIED |
| BUG-19 | Không revoke JWT khi deactivate/reset password | 🟠 | P3-02 | `ChangeUserStatusCommandHandler.cs`, `ResetUserPasswordCommandHandler.cs` | VERIFIED |
| BUG-27 | `GET /api/files` không phân trang | 🟠 | P3-03 | `GetTenantFilesQuery(.Handler).cs` | VERIFIED |
| BUG-28 | `PageSize` không giới hạn trên | 🟠 | P3-03 | `GetUsersWithPaginationQueryHandler.cs` | VERIFIED |
| BUG-36 | Search `.ToLower().Contains` không sargable | 🟡 | P3-03 | `GetUsersWithPaginationQueryHandler.cs` | VERIFIED |
| — | Thiếu index `(TenantId,IsDeleted,CreatedAt)`, `AuditLogs(TenantId,CreatedAt)`, unique `Departments(TenantId,Code)` | 🟡 | P3-03 | configs + migration | VERIFIED |
| BUG-40 | README sai hoàn toàn (.NET 10, Product, Patreon) | 🟢 | P3-04 + P4-05 | `README.md`, `docs/*.md` | VERIFIED |
| — | Docs mô tả 6 endpoint không tồn tại (audit-log, stats, employees, folder, FileShare, refresh) | ❌ | P3-04, P3-06 | `docs/` | VERIFIED |
| BUG-24 | ClamAV mặc định tắt + fail-open | 🟠 | P3-05 | `ClamAvVirusScannerService.cs`, `appsettings.json` | VERIFIED |
| BUG-29 | Email fail bị nuốt, không retry, không báo UI | 🟠 | P3-05 | `EmailService.cs`, các handler gửi mail | VERIFIED |
| BUG-30 | Token invitation lưu plaintext + **log link chứa token** | 🟠 | P3-05 | `EmailService.cs` (+ cân nhắc hash token ở DB) | VERIFIED |
| BUG-31 | HTML injection trong email (tên không escape) | 🟡 | P3-05 | `EmailService.cs` | VERIFIED |
| BUG-F1/F5/F6/F7 | Thiếu chức năng theo docs | ❌ | P3-06 | mới: audit-logs, stats, folders controllers | VERIFIED |
| BUG-38 | CORS `AllowAnyOrigin` + config `Cors:AllowedOrigins` là config chết | 🟡 | P3-07 | `Program.cs` | VERIFIED |
| BUG-25 | Seed tài khoản demo `Admin@123` chạy cả Production | 🟠 | P3-08 | `DbInitializer.cs` | VERIFIED |

---

## Phase 4 — 🟢 Chất lượng

| Bug | Mô tả ngắn | Mức | Task | Files chính | Trạng thái |
|---|---|---|---|---|---|
| — | **0 test, 0 CI** | 🔴 | P4-01, P4-02 | mới: `tests/`, `.github/workflows/` | VERIFIED |
| — | Không có Docker/health check | 🟢 | P4-03 | mới: `Dockerfile`, `docker-compose.yml` | VERIFIED |
| BUG-32 | `LastLoginAt`/`MarkLogin()` dead code | 🟡 | P4-04 | `LoginCommandHandler.cs` | VERIFIED |
| BUG-33 | `LoggingBehavior` không được đăng ký (dead code) | 🟡 | P4-04 | `LoggingBehavior.cs`, `ApplicationServiceExtensions.cs` | VERIFIED |
| BUG-37 | `AddInfrastructureServices(string?)` hard-code `root/root` | 🟡 | P4-04 (hoặc P1-06) | `InfrastructureServiceExtensions.cs` | VERIFIED |
| — | Dead code: `Product*`, `Users/UpdateUser`, `Users/DeactiveUser`; typo `Repositorires`; `FormatBytes` ×5; magic number | 🟢 | P4-04 | nhiều file | VERIFIED |
| — | Benchmark 100 → 100k user | 🟢 | P4-06 | mới: `review/02_BENCHMARK.md` | VERIFIED |

---

## TONG KET THEO MUC DO

| Mức | Tổng bug | Đã VERIFIED |
|---|---|---|
| 🔴 Critical | 13 | 13 |
| 🟠 High | 16 | 16 |
| 🟡 Medium | 9 | 9 |
| 🟢 Low | 4 | 4 |
| *(func-gap ❌)* | 7 endpoint/thiếu chức năng | 7 |
| **Tổng** | **40 bug + 7 thiếu sót** | **47 / 47** |

**Tiến độ theo phase:** Phase 1: 10/10 · Phase 2: 15/15 · Phase 3: 15/15 · Phase 4: 7/7 (Hoàn thành 100%)

> **Quy tắc cập nhật:** không đánh dấu `VERIFIED` nếu chưa chạy lệnh tương ứng trong `06_CHECKLIST_KIEM_NGHIEM.md` và có kết quả dán vào commit/PR.
