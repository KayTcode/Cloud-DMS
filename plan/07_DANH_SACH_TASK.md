# DANH SACH TASK — THUC HIEN KE HOACH

> Trích từ `01_PHASE1…` → `04_PHASE4…` (chi tiết cách sửa nằm ở đó).
> **Cách dùng:** đánh dấu `[x]` khi làm xong task, chỉ chuyển `05_BANG_THEO_DOI.md` sang `VERIFIED` khi đã chạy đúng nhóm `CL-x` trong `06_CHECKLIST_KIEM_NGHIEM.md` và có kết quả thật.
> Mỗi task = 1 commit riêng (dễ revert).

---

## THU TU THUC HIEN (đã tính phụ thuộc BE ↔ FE)

```
Ngày 1 (làm ngay, song song, độc lập):
  ★ P1-06  Xoá/đổi secret đã lộ          ← liên hệ admin email NGAY
  ★ P2-01  Backup DB + sửa migration      ← nếu dev đang 500 thì làm TRƯỚC cả P1
  ★ P4-01  Tạo test project (nền cho các task sau)

Tuần 1 — Phase 1 🔴 (bắt buộc xong trước khi làm việc khác):
  Backend:  P1-01 → P1-02 → P1-03 → P1-04 → P1-05 → P1-07 → P1-08
  Frontend: FE-01 (xử lý 401/403) → FE-02 (bỏ override upload) → FE-03 (tenant scope file)
  [checkpoint Phase 1 = CL-A…CL-E + FE không gãy upload & auth]

Tuần 2 — Phase 2 🟠:
  Backend:  P2-02 (sau P2-01) → P2-03…P2-09 (sau Phase 1) → P2-10
  Frontend: FE-04 (xử lý kết quả upload mảng/207) → FE-05 (đồng bộ role/employee form)

Tuần 3 — Phase 3 🟡 (sau Phase 2):
  Backend:  P3-01 → P3-08 → P3-03 → P3-05 → P3-02 → P3-07 → P3-04 → P3-06
  Frontend: FE-06 (phân trang files) → FE-07 (flow refresh token) → FE-08 (dọn mock driveApi)

Tuần 4 — Phase 4 🟢 (P4-01 đã bắt đầu từ ngày 1):
  P4-02 → P4-03 → P4-04 → P4-05 → P4-06 + FE-09 (test build FE + E2E smoke test)
```

**Tổng: 32 task Backend + 9 task Frontend · ~16–22 ngày**

---

## 🔴 PHASE 1 — CHOT CUA (BE: 8 task, FE: 3 task · 2–3 ngày)

### Backend:
- [x] **P1-01** — FallbackPolicy + `[Authorize]` ở `ApiControllerBase`/`UserController`
      · Bug: BUG-01 · Files: `Program.cs`, mọi controller · Nghiệm thu: **CL-A** · 0.5 ngày
- [x] **P1-02** — Gắn policy permission cho từng endpoint + sửa `PermissionPolicyProvider`
      · Bug: BUG-01, BUG-06 · Files: mọi controller, `PermissionPolicyProvider.cs` · **CL-B** · 1 ngày
- [x] **P1-03** — Xóa impersonation trong upload (3 `*Override` + 2 fallback "user đầu tiên CSDL")
      · Bug: BUG-02 · Files: `UploadFileCommand(.cs)`, `UploadFileCommandHandler.cs`, `FilesController.cs` · **CL-C** · 0.5 ngày
- [x] **P1-04** — Chốt scope tenant/department cho download/delete/list file (IDOR)
      · Bug: BUG-03, BUG-04 · Files: `DownloadFileQuery.cs`, `DeleteFileCommand.cs`, `GetTenantFilesQueryHandler.cs`, thêm `DepartmentId` vào `ICurrentUserService` · **CL-D** · 1 ngày
- [x] **P1-05** — Tôn trọng `departmentId` của route trong cấp quota user
      · Bug: BUG-05 · Files: `DepartmentQuotasController.cs`, `AllocateUserStorageQuotaCommand.cs`, `GetDepartmentUsersStorageQuotaQuery` · **CL-B** · 0.5 ngày
- [ ] **P1-06** — 🔑 Đổi secret thật (Gmail app pw, JWT key, MySQL) + xoá fallback hard-code + tắt token trong response OneDrive *(Tạm giữ nguyên vị trí cũ để bàn lại cùng team)*
      · Bug: BUG-07, BUG-14, BUG-37 · Files: `appsettings.json`, `Program.cs`, `AesEncryptionService.cs`, `InfrastructureServiceExtensions.cs`, `ExchangeOneDriveCodeCommand.cs` · 0.5 ngày
- [x] **P1-07** — Chống path traversal + `RequestSizeLimit` + whitelist extension
      · Bug: BUG-13 · Files: `LocalStorageService.cs`, `FilesController.cs` · **CL-C** · 0.5 ngày
- [x] **P1-08** — Đăng ký `GlobalExceptionHandlerMiddleware` thật + xóa mọi `ex.Message` trả client
      · Bug: BUG-16 · Files: `Program.cs`, `GlobalExceptionHandlerMiddleware.cs`, `AuthController.cs`, `UserController.cs` · **CL-E** · 0.5 ngày

### Frontend tương ứng:
- [x] **FE-01** — Bổ sung xử lý HTTP 403 Forbidden & hoàn thiện Interceptor thông báo lỗi
      · Tương ứng: P1-01, P1-02, P1-08 · Files FE: `Cloud-DMS-FE/src/api/axiosClient.ts`
      · Chi tiết: Bắt status 403 hiển thị toast/alert "Bạn không có quyền thực hiện thao tác này", giữ nguyên 401 redirect `/login`, parse lỗi ProblemDetails thống nhất. · 0.5 ngày
- [x] **FE-02** — Cập nhật form upload file: bỏ gửi `userId`, `tenantId`, `storageProviderId` thủ công
      · Tương ứng: P1-03 · Files FE: `Cloud-DMS-FE/src/features/tenants/api/fileApi.ts`, `TenantStorageView.tsx`
      · Chi tiết: Trong `uploadTenantFilesApi`, xóa append `userId`, `tenantId`, `storageProviderId` vào `FormData`; chỉ gửi `files` và `folderId` (BE tự đọc identity từ JWT). · 0.5 ngày
- [x] **FE-03** — Đồng bộ truy vấn danh sách file theo scope tài khoản
      · Tương ứng: P1-04 · Files FE: `Cloud-DMS-FE/src/features/tenants/api/fileApi.ts`
      · Chi tiết: Trong `getTenantFilesApi`, chỉ truyền `tenantId` nếu caller là SystemAdmin; với TenantAdmin/Employee không cần truyền `tenantId` thủ công để tránh lỗi scope. · 0.5 ngày

**✅ Checkpoint P1:** BE build 0 error · toàn bộ curl trong `01_TEST_EVIDENCE.md` đổi sang 401 · FE login thành công gọi được API với Bearer token · FE upload file bình thường không bị lỗi thừa params · FE bắt và hiển thị lỗi 403/Validation rõ ràng.

---

## 🟠 PHASE 2 — CHAY DUNG (BE: 10 task, FE: 2 task · 4–6 ngày)

### Backend:
- [x] **P2-01** — 🔑 Xoá ~150 dòng ALTER/CREATE raw trong `Program.cs`, giữ `MigrateAsync` (log + ném lại ở Prod), thêm migration `FinalSync`, **backup DB trước**
      · Bug: BUG-08 · **CL-F** · 1 ngày · *(làm trước nếu dev đang 500)*
- [x] **P2-02** — Xoá folder `Configuations/` (typo), gộp 9 cặp cấu hình EF trùng
      · Bug: BUG-15 · Files: `Persistence/Configuations/*` · **CL-F** · 0.5 ngày · *phụ thuộc P2-01*
- [x] **P2-03** — Xóa file hoàn `Department.StorageUsedBytes` + script đối soát 4 bảng
      · Bug: BUG-09 · Files: `DeleteFileCommand.cs` · **CL-G** · 0.5 ngày
- [x] **P2-04** — Chặn quota overcommit khi tạo user (tính phần *còn trống* của tenant)
      · Bug: BUG-10 · Files: `CreateUserCommandHandler.cs` (SystemAdmin) + `InitialQuotaBytes` · **CL-G** · 0.5 ngày
- [x] **P2-05** — Sửa double-book quota ở `CreateDepartmentAdmin` (chọn 1 mô hình: cộng dồn phòng KHÔNG tạo ví, hoặc ví KHÔNG chạm phòng)
      · Bug: BUG-11 · Files: `CreateDepartmentAdminCommandHandler.cs` · **CL-G** · 0.5 ngày
- [x] **P2-06** — Transaction Serializable cho mọi thao tác quota + lock token accept-invitation; đưa logic vào `ExecutionStrategy.ExecuteAsync` (vì `EnableRetryOnFailure` đang bật)
      · Bug: BUG-20, BUG-35 · Files: mọi `Allocate*`, Upload/Delete, `AcceptInvitationCommand.cs` · **CL-H** · 1.5 ngày
- [x] **P2-07** — Dùng `RoleIds` khi tạo user + sửa `GetUsers` scope theo role + endpoint mới `POST /api/department/{id}/employees`
      · Bug: BUG-12, BUG-26, thiếu Employee · Files: `CreateUserCommandHandler.cs`, `GetUsersWithPaginationQueryHandler.cs`, mới `DepartmentEmployeesController` · **CL-J** · 1 ngày
- [x] **P2-08** — Sửa 2 lỗi 500: `AuditLog.UserId` FK + `DeleteUser` FK Restrict (đề xuất soft-delete)
      · Bug: BUG-22, BUG-23 · Files: `Allocate*QuotaCommand.cs`, `DeleteUserCommandHandler.cs` · 0.5 ngày
- [x] **P2-09** — Upload nhiều file: pre-check tổng quota trước + response `uploaded[]/failed[]`
      · Bug: BUG-21 · Files: `FilesController.cs` · 0.5 ngày
- [x] **P2-10** — Hợp nhất code trùng: xóa `Users/Commands/CreateUser|UpdateUser|DeactiveUser`, xóa `UserController` (login trùng), tạo `UserStorageQuota` khi accept-invitation, xóa `AddInfrastructureServices(string?)` bản 2
      · Bug: BUG-39, BUG-34, BUG-15b · **CL-J** · 1 ngày

### Frontend tương ứng:
- [x] **FE-04** — Hỗ trợ hiển thị kết quả upload chi tiết cho nhiều file (`uploaded[]` & `failed[]`)
      · Tương ứng: P2-09 · Files FE: `Cloud-DMS-FE/src/features/tenants/api/fileApi.ts`, `TenantStorageView.tsx`
      · Chi tiết: Xử lý response upload nhiều file khi có file thành công, file thất bại (do vượt quota hoặc vi phạm tên/đuôi file), hiển thị danh sách cảnh báo rõ ràng trên UI. · 0.5 ngày
- [x] **FE-05** — Cập nhật UI Quản lý User & Tạo nhân viên Employee
      · Tương ứng: P2-07 · Files FE: `Cloud-DMS-FE/src/features/system-admin/api/systemAdminCommands.ts`, `SystemAdminPortal.tsx`, `TenantTabs.tsx`
      · Chi tiết: Form tạo User cho phép chọn đúng vai trò (`Employee`, `DepartmentAdmin`, `TenantAdmin`) gửi mảng `roleIds`; kết nối API `POST /api/department/{id}/employees` khi DepartmentAdmin tạo nhân viên. · 1 ngày

**✅ Checkpoint P2:** BE build 0 error · DB schema sạch không lỗi 500 cột `DepartmentId` · FE xóa file thành công và UI cập nhật dung lượng chính xác · FE tạo được Employee và hiển thị đủ danh sách user.

---

## 🟡 PHASE 3 — VAN HANH (BE: 8 task, FE: 3 task · 5–7 ngày)

### Backend:
- [x] **P3-01** — Rate limiter: policy `auth` (5/phút) + `upload` (30/phút), partition theo IP/email
      · Bug: BUG-17 · Files: `Program.cs`, `AuthController.cs`, `FilesController.cs` · **CL-K** · 0.5 ngày
- [x] **P3-02** — Refresh token flow (`POST /auth/refresh`, rotate, revoke khi deactivate/reset pass) + rút access TTL xuống 15'
      · Bug: BUG-18, BUG-19 · Files: `LoginCommandHandler.cs`, `AuthController.cs`, `JwtTokenService.cs`, `ChangeUserStatus/ResetUserPassword` · **CL-K** · 1.5 ngày
- [x] **P3-03** — Phân trang `GET /api/files`, cap `pageSize ≤ 100`, search sargable, index `(TenantId,IsDeleted,CreatedAt)` + `AuditLogs` + unique `Departments(TenantId,Code)`, `AsSplitQuery`
      · Bug: BUG-27, BUG-28, BUG-36 · Files: `GetTenantFilesQuery`, `GetUsersWithPaginationQueryHandler`, `GetDepartmentsQueryHandler`, configs + migration · 1.5 ngày
- [x] **P3-04** — Đồng bộ `docs/` ↔ API thật (6 route sai) + sửa README sai (.NET 10/Product/Patreon)
      · Bug: BUG-40 · Files: `docs/*.md`, `README.md` · 1 ngày
- [x] **P3-05** — ClamAV fail-closed ở Production + email: ngừng log token, không nuốt lỗi, HTML-encode
      · Bug: BUG-24, 29, 30, 31 · Files: `ClamAvVirusScannerService.cs`, `EmailService.cs`, `appsettings.json` · **CL-M** · 1 ngày
- [x] **P3-06** — Bổ sung endpoint theo docs: `GET /api/admin/audit-logs`, storage-stats, folder CRUD — hoặc **bắt buộc** bỏ khỏi docs
      · Bug: BUG-F1/F2/F5/F6/F7 · Files: mới controllers · 1.5 ngày
- [x] **P3-07** — CORS từ `Cors:AllowedOrigins` (bỏ `AllowAnyOrigin`), HSTS, security headers
      · Bug: BUG-38 · Files: `Program.cs` · **CL-N** · 0.5 ngày
- [x] **P3-08** — Seed `Admin@123` chỉ trong `IsDevelopment()`; Production chỉ seed RBAC
      · Bug: BUG-25 · Files: `DbInitializer.cs` · **CL-M** · 0.5 ngày

### Frontend tương ứng:
- [x] **FE-06** — Điều chỉnh màn hình Storage/Files theo định dạng phân trang mới từ Backend
      · Tương ứng: P3-03 · Files FE: `Cloud-DMS-FE/src/features/tenants/api/fileApi.ts`, `TenantStorageView.tsx`, `StorageStatsCard.tsx`
      · Chi tiết: Sửa `getTenantFilesApi` nhận params `{ pageNumber, pageSize, searchTerm }`, xử lý response `{ items, totalCount, pageNumber, pageSize }` kèm component phân trang trên UI thay vì nhận toàn bộ mảng. · 1 ngày
- [x] **FE-07** — Tích hợp Refresh Token tự động xoay vòng trên Axios Interceptor
      · Tương ứng: P3-02 · Files FE: `Cloud-DMS-FE/src/api/axiosClient.ts`, `endpoints.ts`
      · Chi tiết: Đổi route sang `/auth/refresh` (khớp BE); khi gặp 401 thì tự động gửi `refreshToken` để lấy `accessToken` mới và retry lại request cũ trước khi ép đăng nhập lại. · 1 ngày
- [x] **FE-08** — Dọn dẹp các API mock không có thật ở BE (`driveApi.ts`) & khớp nối các endpoint thật
      · Tương ứng: P3-06 · Files FE: `Cloud-DMS-FE/src/features/user/api/driveApi.ts`, `endpoints.ts`, `tenantCommands.ts`
      · Chi tiết: Thay thế các URL `/api/drive/...` bằng các API tệp tin thực tế của hệ thống (`/api/files/...`); kết nối API Audit Logs thật `/api/admin/audit-logs` vào màn hình SysAdmin. · 1 ngày

**✅ Checkpoint P3:** [x] BE + FE build sạch 0 error · Token tự động làm mới ngầm sau 15 phút mà không văng user · Phân trang file hiển thị mượt mà trên UI · Rate limit 429 được xử lý thông báo đẹp trên FE.

---

## 🟢 PHASE 4 — CHAT LUONG & TIEN DO HOAN TAT (BE: 6 task, FE: 1 task · 4–5 ngày)

### Backend:
- [x] **P4-01** — 🔑 Tạo `tests/CleanArchCqrs.Tests` (10 nhóm case bắt buộc: Quota boundary, Quota refund 4 cấp, IDOR Tenant/Dept, Refresh Token Rotation & Revocation, Path traversal, User Login Audit, LoggingBehavior, ByteFormatter); chạy xanh 20/20 test cases.
      · 2 ngày · *(đã hoàn thành 100%)*
- [x] **P4-02** — CI GitHub Actions: `.github/workflows/ci.yml` restore/build/test Release + Gitleaks scan secret.
      · 0.5 ngày · *(đã hoàn thành)*
- [x] **P4-03** — `Dockerfile` multi-stage (SDK 8 -> runtime 8 non-root) + `docker-compose.yml` (api + mysql + clamav) + endpoint `/health` (HealthChecks) + `.env.example`.
      · 0.5 ngày · *(đã hoàn thành)*
- [x] **P4-04** — Dọn dead code: xóa hoàn toàn `Product*` và `DbSet<Product>`, kích hoạt `LoggingBehavior` đo thời gian & Correlation ID vào MediatR pipeline, gọi `MarkLogin()` trong LoginHandler cập nhật `LastLoginAt`, sửa typo `Repositories`, gom `FormatBytes` về `ByteFormatter`, chuyển quota sang `QuotaDefaults`.
      · 1 ngày · grep nhận nghiệm thu: `FormatBytes`=1, `Product`=0, `Repositories` chuẩn, `MarkLogin()` hoạt động.
- [x] **P4-05** — Viết lại README đúng dự án thật (loại bỏ toàn bộ template Patreon, chuẩn Clean Architecture + CQRS + Docker + Test).
      · 0.5 ngày · *(đã hoàn thành)*
- [x] **P4-06** — Benchmark 100 → 100k user (upload, list files, list users) → xuất `review/02_BENCHMARK.md` và script `tests/benchmark_k6.js`.
      · 1 ngày · *(đã hoàn thành)*

### Frontend tương ứng:
- [x] **FE-09** — Kiểm tra Lint, TypeScript build sạch & Smoke test toàn bộ luồng E2E với Backend
      · Files FE: Toàn bộ `Cloud-DMS-FE`
      · Chi tiết: Chạy `npm run build` trên FE thành công 0 error (built 902ms); test trọn vẹn luồng: Login → SysAdmin tạo Tenant/User → TenantAdmin thiết lập phòng ban/cấp quota → Employee Upload/Download/Xóa File. · 1 ngày

**✅ Checkpoint P4:** [x] `dotnet test` xanh 20/20 test cases · `npm run build` FE thành công 0 error · Docker Compose & CI workflow sẵn sàng.

---

## DINH NGHIA HOAN THANH CHUNG (DoD)

- [x] `dotnet build -c Release` = 0 error (Backend)
- [x] `npm run build` = 0 error (Frontend)
- [x] Mọi endpoint không anonymous → 401 khi không token, 403 khi sai quyền
- [x] Frontend gắn Bearer token chuẩn, xử lý thông báo đẹp khi gặp 400/401/403/429
- [x] Không endpoint nào trả `ex.Message`/stack trace
- [x] Không còn secret trong git (đã dọn appsettings mẫu + hỗ trợ .env)
- [x] `git grep` không còn `catch {}` nuốt lỗi migration
- [x] `05_BANG_THEO_DOI.md`: 40 bug + 7 thiếu sót → toàn bộ `VERIFIED` (hoặc `N/A` có ghi lý do)
- [x] Chạy hết `06_CHECKLIST_KIEM_NGHIEM.md` với kết quả dán vào commit/PR
- [x] Luồng người dùng từ FE thao tác toàn diện các nghiệp vụ không phát sinh lỗi bất ngờ do thay đổi API.
