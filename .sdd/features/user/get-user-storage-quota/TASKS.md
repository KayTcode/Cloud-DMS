# TASKS: Get User Storage Quota (Xem Hạn Mức Dung Lượng Cá Nhân)

- **Feature Slug:** `user/get-user-storage-quota`
- **Module:** `User Portal / Storage Quota`
- **Spec Reference:** `SPEC.md` (v1.0.0 — `APPROVED`)
- **Plan Reference:** `PLAN.md` (v1.0.0 — `APPROVED`)
- **Status:** `COMPLETED`

---

## 1. Task List & Breakdown

### Giai đoạn 1: Backend Quota Query & DTO (Tầng Application)

#### T001 [x] — Tạo PersonalStorageQuotaDto trong Tầng Application
- **Intent reference:** `SPEC.md` EARS-QT-01 & `PLAN.md` Section 1.2, 5.1
- **Input and expected outcome:**
  - *Input:* Chưa có DTO chuẩn để trả về hạn mức lưu trữ cá nhân của người dùng.
  - *Outcome:* Tạo `PersonalStorageQuotaDto.cs` chứa các trường: `UserId`, `FullName`, `QuotaBytes`, `UsedBytes`, `AvailableBytes`, `UsagePercent`, `DepartmentName`.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Users/DTOs/PersonalStorageQuotaDto.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: none
- **Estimated effort:** 15 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Khai báo DTO dùng chung giữa Application và Presentation.
- **High-risk review route:** `Standard Review`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T002 [x] — Tạo GetMyStorageQuotaQuery & Handler trong Tầng Application
- **Intent reference:** `SPEC.md` EARS-QT-01 & `PLAN.md` Section 2, 3
- **Input and expected outcome:**
  - *Input:* Người dùng cần tra cứu hạn mức cá nhân của chính mình dựa trên JWT token (`ICurrentUserService.UserId`).
  - *Outcome:* Tạo `GetMyStorageQuotaQuery` kế thừa `IRequest<Result<PersonalStorageQuotaDto>>`. Trong `GetMyStorageQuotaQueryHandler`:
    1. Lấy `UserId` từ `ICurrentUserService`. Nếu rỗng, trả về `Result.Failure("Không tìm thấy thông tin xác thực người dùng.")`.
    2. Truy vấn `UserStorageQuotas` theo `UserId`.
    3. Nếu không tìm thấy, trả về `Result.Failure("Không tìm thấy thông tin hạn mức dung lượng của người dùng.")`.
    4. Tính toán `AvailableBytes = Math.Max(0, QuotaBytes - UsedBytes)`, `UsagePercent = QuotaBytes > 0 ? Math.Round((double)UsedBytes / QuotaBytes * 100, 2) : 0`.
    5. Trả về `Result<PersonalStorageQuotaDto>.Success(...)`.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Users/Queries/GetMyStorageQuota/GetMyStorageQuotaQuery.cs`
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Users/Queries/GetMyStorageQuota/GetMyStorageQuotaQueryHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T001`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `none` (Read-only query)
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Tích hợp MediatR Pipeline.
- **High-risk review route:** `Standard Review`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 2: API Controller Endpoint (Tầng Presentation)

#### T003 [x] — Tạo UserQuotaController với Endpoint GET /api/users/me/quota
- **Intent reference:** `SPEC.md` EARS-QT-01 & `PLAN.md` Section 2
- **Input and expected outcome:**
  - *Input:* Chưa có controller tiếp nhận request lấy quota cá nhân của người dùng.
  - *Outcome:* Tạo `UserQuotaController.cs` kế thừa `BaseApiController` với route `api/users`:
    - `[HttpGet("me/quota")]`: Gửi `GetMyStorageQuotaQuery` qua MediatR, trả về `Ok(result.Value)` hoặc `BadRequest(result.Error)`.
    - Bảo vệ bằng `[Authorize]`.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.API/Controllers/UserQuotaController.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T002`
- **Estimated effort:** 20 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.API/CleanArchCqrs.API.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Expose endpoint Swagger và RESTful API.
- **High-risk review route:** `Standard Review`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 3: Frontend API Client & State Binding

#### T004 [x] — Cập Nhật driveApi.ts & Types Cho Quota Cá Nhân
- **Intent reference:** `SPEC.md` EARS-QT-01, 02 & `PLAN.md` Section 5.1, 5.2
- **Input and expected outcome:**
  - *Input:* Frontend chưa có interface `PersonalStorageQuotaDto` và phương thức gọi `GET /api/users/me/quota`.
  - *Outcome:*
    1. Bổ sung interface `PersonalStorageQuotaDto` trong `src/features/user/types/index.ts`.
    2. Thêm phương thức `getMyQuota` vào `userApi` trong `src/features/user/api/driveApi.ts` gọi `axiosClient.get('/users/me/quota')`.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/user/types/index.ts`
  - `Cloud-DMS-FE/src/features/user/api/driveApi.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T003`
- **Estimated effort:** 20 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm run build --prefix "Cloud-DMS-FE"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Đồng bộ DTO frontend và backend.
- **High-risk review route:** `Standard Review`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T005 [x] — Nạp Dữ Liệu Thực Tế Vào StorageView.tsx
- **Intent reference:** `SPEC.md` EARS-QT-01, 02 & `PLAN.md` Section 1.2
- **Input and expected outcome:**
  - *Input:* `StorageView.tsx` đang hiển thị dữ liệu tĩnh hardcode (1.0 TB).
  - *Outcome:*
    1. Gọi `userApi.getMyQuota()` khi mount component qua `useEffect`.
    2. Hiển thị đúng 3 thẻ chỉ số: "Tổng dung lượng được cấp", "Đã sử dụng", "Còn khả dụng" theo dữ liệu byte trả về từ backend (được format chuẩn GB/MB).
    3. Thanh tiến trình hiển thị màu sắc theo đúng ngưỡng:
       - Xanh lá cây khi < 80%.
       - Vàng cam (`text-amber-500`, `bg-amber-500`) khi từ 80% đến dưới 95%.
       - Đỏ cảnh báo (`text-rose-500`, `bg-rose-500`) khi >= 95%.
    4. Xử lý trạng thái Loading Skeleton và Error Alert nếu không tải được quota.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/user/views/StorageView.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T004`
- **Estimated effort:** 35 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm run build --prefix "Cloud-DMS-FE"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Cập nhật UI hiển thị trực quan dung lượng người dùng.
- **High-risk review route:** `Standard Review`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

## 2. Traceability Matrix

| Mã Requirement | Mã Task | Trạng thái | Ghi chú xác thực |
| :--- | :--- | :--- | :--- |
| **EARS-QT-01** | `T001`, `T002`, `T003`, `T004`, `T005` | Completed | Endpoint GET /api/users/me/quota & hiển thị StorageView |
| **EARS-QT-02** | `T005` | Completed | Đổi màu thanh tiến trình Quota theo ngưỡng <80%, >=80%, >=95% |

---

## 3. Execution Sequencing
```text
T001 (DTO) [DONE] ──> T002 (Query & Handler) [DONE] ──> T003 (Controller) [DONE] ──> T004 (FE API Client) [DONE] ──> T005 (StorageView UI) [DONE]
```

---

## 4. AI Agent Recommendation & Human Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Đề xuất danh sách các task T001..T005 cho tính năng xem dung lượng lưu trữ cá nhân để Human Reviewer xem xét và phê duyệt trước khi thực thi.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt danh sách nhiệm vụ (TASKS.md) cho tính năng get-user-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED in the persisted review block.
```

### Human Final Review
- **Status:** `PENDING`

