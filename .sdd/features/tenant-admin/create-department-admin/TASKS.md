# TASKS: Create Department Admin (Bổ nhiệm Trưởng phòng Ban)

- **Feature Slug:** `create-department-admin`
- **Module:** `TenantAdmin`
- **Spec Version:** `1.1.0`
- **Plan Reference:** `PLAN.md` (v1.1.0 — `APPROVED`)
- **Status:** `APPROVED`

---

## 1. Task List & Breakdown

### Giai đoạn 1: Triển khai Backend (.NET Clean Architecture)

#### T001 [x] — Cập nhật Command DTO và API Controller Request DTO (Loại bỏ trường Password)
- **Intent reference:** Intent Packet & `EARS-04`
- **Input and expected outcome:**
  - *Input:* `CreateDepartmentAdminCommand` và `CreateDepartmentAdminRequest` hiện có thuộc tính `string? Password`.
  - *Outcome:* Loại bỏ hoàn toàn trường `Password`. Controller không còn truyền mật khẩu vào Command.
- **Layer and file boundary:**
  - `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommand.cs`
  - `CleanArchCqrs.API/Controllers/Tenant/DepartmentAdminsController.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: none
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `shared/public contract`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Thay đổi public request payload của `POST /api/tenant/{tenantId}/department-admins`. Cần thông báo đồng bộ sang Frontend.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T002 [x] — Cập nhật Command Validator (Xóa quy tắc kiểm tra mật khẩu)
- **Intent reference:** `EARS-04`
- **Input and expected outcome:**
  - *Input:* `CreateDepartmentAdminCommandValidator.cs` đang có rule kiểm tra độ dài `RuleFor(v => v.Password)`.
  - *Outcome:* Xóa bỏ rule kiểm tra password; giữ nguyên các quy tắc cho `TenantId`, `Email`, `FirstName`, `LastName`, `PhoneNumber`.
- **Layer and file boundary:**
  - `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandValidator.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T001`
- **Estimated effort:** 15 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `no impact`
- **High-risk review route:** `N/A`
- **Post-code review:** `N/A`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T003 [x] — Cập nhật Handler (Xử lý Re-creation tài khoản xóa mềm & Luồng 100% Email Invitation)
- **Intent reference:** Intent Packet, `EARS-01`, `EARS-01A`, `EARS-04`, `EARS-05`, `EARS-06`..`10`
- **Input and expected outcome:**
  - *Input:* Handler hiện tại chặn cứng email trùng lặp bất kể trạng thái; phụ thuộc vào cờ `isInvitation` từ `request.Password`.
  - *Outcome:*
    1. Kiểm tra email: Nếu trùng và `IsActive = true` -> trả lỗi duplicate. Nếu trùng nhưng `IsActive = false` -> đổi tên email bản ghi cũ sang `{baseEmail}__deleted_{timestamp}` để giải phóng Unique Index.
    2. Khởi tạo tài khoản: Luôn sinh mật khẩu ngẫu nhiên tạm thời (GUID băm BCrypt), đặt `IsActive = false` và `EmailConfirmed = false`.
    3. Luôn sinh `EmailVerificationToken` (loại `DepartmentAdminInvitation`, hạn 48 giờ).
    4. Ghi `AuditLog` với action `"InviteDepartmentAdminWithQuota"`.
    5. Gửi thư mời qua `IEmailService.SendDepartmentAdminInvitationAsync` có `try/catch` bọc an toàn để tránh gián đoạn DB transaction khi lỗi SMTP.
- **Layer and file boundary:**
  - `CleanArchCqrs.Application/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T001`, `T002`
- **Estimated effort:** 1 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `business-data mutation & external side effect`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `no impact`
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T004 [x] — Viết Unit Tests xác thực toàn diện cho CreateDepartmentAdminCommandHandler
- **Intent reference:** `SPEC.md` Section 6 Acceptance Criteria (BDD Scenarios 1–4)
- **Input and expected outcome:**
  - *Input:* Chưa có test case chuyên biệt cho các điều kiện mới của `CreateDepartmentAdmin`.
  - *Outcome:* Tạo file test `CreateDepartmentAdminCommandHandlerTests.cs` kiểm định đầy đủ 5 trường hợp:
    1. Bổ nhiệm thành công qua Email Invitation (IsActive = false, EmailConfirmed = false, sinh token 48h, gửi email).
    2. Cho phép tái tạo tài khoản với email của người dùng đã bị xóa mềm (`IsActive = false`).
    3. Từ chối tạo tài khoản khi email đã tồn tại và đang hoạt động (`IsActive = true`).
    4. Từ chối bổ nhiệm khi phòng ban đã có Trưởng phòng (1:1).
    5. Từ chối bổ nhiệm khi hạn mức yêu cầu vượt quá dung lượng khả dụng của phòng ban/Tenant.
- **Layer and file boundary:**
  - `Cloud-DMS/tests/CleanArchCqrs.Tests/TenantAdmin/CreateDepartmentAdminCommandHandlerTests.cs`
- **Owner and dependencies:** AI Agent / QA Engineer; `blockedBy`: `T003`
- **Estimated effort:** 1.5 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet test "Cloud-DMS/tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj" --filter "FullyQualifiedName~CreateDepartmentAdmin"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `no impact`
- **High-risk review route:** `N/A`
- **Post-code review:** `N/A`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T005 [x] — Build & Regression Verification toàn bộ Backend Solution
- **Intent reference:** Definition of Done
- **Input and expected outcome:**
  - *Input:* Toàn bộ thay đổi mã nguồn và test suite đã hoàn thành.
  - *Outcome:* Solution build thành công, không có cảnh báo/lỗi, toàn bộ bài test vượt qua (`PASSED`).
- **Layer and file boundary:** Toàn bộ solution `Cloud-DMS`
- **Owner and dependencies:** AI Agent / Tech Lead; `blockedBy`: `T004`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet test "Cloud-DMS/tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `/sdd-trace` và `/sdd-sync`
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 2: Triển khai Frontend (Cloud-DMS-FE)

#### T006 [x] — Cập nhật Frontend DTO Types (Loại bỏ trường password)
- **Intent reference:** `EARS-04` & Frontend Contract Sync
- **Input and expected outcome:**
  - *Input:* `CreateDepartmentAdminPayload` trong `tenantUser.dto.ts` hiện có thuộc tính bắt buộc `password: string;`.
  - *Outcome:* Loại bỏ trường `password` khỏi interface `CreateDepartmentAdminPayload` để đồng bộ hoàn toàn với Backend API contract.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/tenants/types/tenantUser.dto.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T005`
- **Estimated effort:** 15 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `shared/public contract`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Đồng bộ contract giữa Client và Backend
- **High-risk review route:** `N/A`
- **Post-code review:** `N/A`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T007 [x] — Cập nhật Giao diện Bổ nhiệm Trưởng phòng (Loại bỏ ô nhập Password & Hiển thị thông báo thư mời Email)
- **Intent reference:** User Experience & `EARS-01`, `EARS-04`
- **Input and expected outcome:**
  - *Input:* Giao diện [CreateDepartmentUserView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/tenants/views/CreateDepartmentUserView.tsx) có các trường nhập mật khẩu ban đầu, nút tự tạo mật khẩu, state `password`, và gửi `password` trong hàm submit.
  - *Outcome:*
    1. Loại bỏ các state và UI liên quan đến `password` và `showPassword`.
    2. Bổ sung banner thông báo trực quan: *"Tài khoản sẽ được tạo ở trạng thái chờ kích hoạt. Hệ thống sẽ tự động gửi email kèm đường dẫn bảo mật (hiệu lực 48 giờ) để Trưởng phòng tự thiết lập mật khẩu lần đầu."*
    3. Cập nhật hàm gọi `handleCreateDepartmentAdmin` không còn truyền `password`.
    4. Cập nhật [TenantContext.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/context/TenantContext.tsx) nếu có mock/fallback liên quan đến password.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/tenants/views/CreateDepartmentUserView.tsx`
  - `Cloud-DMS-FE/src/context/TenantContext.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T006`
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `no impact`
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T008 [x] — Kiểm thử End-to-End & Build Frontend
- **Intent reference:** Definition of Done
- **Input and expected outcome:**
  - *Input:* Toàn bộ code Frontend đã được cập nhật.
  - *Outcome:* Build sản phẩm Frontend qua Vite thành công 0 lỗi TypeScript và cú pháp.
- **Layer and file boundary:** Toàn bộ project `Cloud-DMS-FE`
- **Owner and dependencies:** AI Agent / Frontend Lead; `blockedBy`: `T007`
- **Estimated effort:** 20 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run build
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `/sdd-trace` và `/sdd-sync`
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

## 2. DoD (Definition of Done)

- [x] Mọi task đều có ranh giới file, layer, owner và command kiểm định chính xác.
- [x] Mỗi task độc lập hoặc khai báo rõ `blockedBy`.
- [x] Đã phân tách rõ ràng 2 giai đoạn: Giai đoạn 1 (Backend T001–T005), Giai đoạn 2 (Frontend T006–T008).
- [x] Sizing tín hiệu nằm trong định mức (`within-guideline`).
- [x] Phạm vi task bám sát 100% `SPEC.md` v1.1.0 và `PLAN.md` v1.1.0 (`APPROVED`).
- [x] Đã thiết lập đầy đủ bài kiểm thử đơn vị và build verification cho cả hai nền tảng.

---

## 3. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Toàn bộ 8 tasks (T001–T008) thuộc cả hai giai đoạn Backend (.NET Clean Architecture) và Frontend (React/Vite) đã được thực thi và kiểm định thành công (Backend 6/6 tests Passed; Frontend Lint & Build Passed 100%). Kính đề nghị Human Reviewer kiểm tra và đóng task.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Thực hiện theo lộ trình 2 giai đoạn: Backend (T001–T005) sau đó Frontend (T006–T008).
- **Timestamp:** 2026-10-13T14:55:00+07:00

---

## 4. Execution Record & Verification Summary

### Execution Details:
- **Execution Mode:** `direct`
- **Tasks Executed:** T001, T002, T003, T004, T005, T006, T007, T008 (8/8 Tasks COMPLETED)

| Task | Boundary | Status | Verification Command | Result |
| :--- | :--- | :--- | :--- | :--- |
| **T001** | `CreateDepartmentAdminCommand.cs`, `DepartmentAdminsController.cs` | `COMPLETED` | `dotnet build Cloud-DMS/src/CleanArchCqrs.Application` | `0 Warning, 0 Error` |
| **T002** | `CreateDepartmentAdminCommandValidator.cs` | `COMPLETED` | `dotnet build Cloud-DMS/src/CleanArchCqrs.Application` | `0 Warning, 0 Error` |
| **T003** | `CreateDepartmentAdminCommandHandler.cs` | `COMPLETED` | `dotnet build Cloud-DMS/src/CleanArchCqrs.Application` | `0 Warning, 0 Error` |
| **T004** | `CreateDepartmentAdminCommandHandlerTests.cs` | `COMPLETED` | `dotnet test --filter "FullyQualifiedName~CreateDepartmentAdmin"` | `Passed 6/6 (100%)` |
| **T005** | Backend Regression & Integration | `COMPLETED` | `dotnet test` | `0 Failed, 6 Passed` |
| **T006** | `tenantUser.dto.ts` | `COMPLETED` | `npm --prefix Cloud-DMS-FE run lint` | `Passed (Exit code 0)` |
| **T007** | `CreateDepartmentUserView.tsx`, `TenantContext.tsx` | `COMPLETED` | `npm --prefix Cloud-DMS-FE run lint` | `Passed (Exit code 0)` |
| **T008** | `Cloud-DMS-FE` Build & Bundle | `COMPLETED` | `npm --prefix Cloud-DMS-FE run build` | `Built in 1.37s (Exit code 0)` |
