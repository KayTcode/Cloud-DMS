# TASKS: Master User Portal Operations & File Integration

- **Feature Slug:** `user`
- **Module:** `User Portal (Personal Document Management)`
- **Spec Reference:** `SPEC.md` (v1.0.0 — `APPROVED`)
- **Plan Reference:** `PLAN.md` (v1.0.0 — `APPROVED`)
- **Status:** `COMPLETED`

---

## 1. Task List & Breakdown

### Giai đoạn 1: Backend Quota API Cho Người Dùng

#### T001 [x] — Tạo DTO & Query Tra Cứu Hạn Mức Cá Nhân (GetMyStorageQuota)
- **Intent reference:** `SPEC.md` EARS-QT-01 & `PLAN.md` Section 1.2, 5.1
- **Input and expected outcome:**
  - *Input:* Cần cung cấp API để người dùng xem hạn mức `UserStorageQuota` của chính mình.
  - *Outcome:*
    1. Tạo `PersonalStorageQuotaDto.cs` trong `CleanArchCqrs.Application/Features/Users/DTOs/`.
    2. Tạo `GetMyStorageQuotaQuery.cs` & `GetMyStorageQuotaQueryHandler.cs` trong `CleanArchCqrs.Application/Features/Users/Queries/GetMyStorageQuota/`.
    3. Đọc dữ liệu từ `_context.UserStorageQuotas` theo `_currentUserService.UserId`, tính toán `AvailableBytes` và `UsagePercent`.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Users/DTOs/PersonalStorageQuotaDto.cs`
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Users/Queries/GetMyStorageQuota/GetMyStorageQuotaQuery.cs`
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Users/Queries/GetMyStorageQuota/GetMyStorageQuotaQueryHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: none
- **Estimated effort:** 30 phút
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `none` (Read-only query)
- **High-risk review route:** `Standard Review`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T002 [x] — Tạo Endpoint GET /api/users/me/quota trong API Controller
- **Intent reference:** `SPEC.md` EARS-QT-01 & `PLAN.md` Section 2
- **Input and expected outcome:**
  - *Input:* Endpoint công khai cho frontend gọi tra cứu quota cá nhân.
  - *Outcome:* Tạo `UserQuotaController.cs` (hoặc mở rộng `UsersController.cs`) kế thừa `BaseApiController`:
    - `[HttpGet("me/quota")]`: Gửi `GetMyStorageQuotaQuery` và trả về `Result<PersonalStorageQuotaDto>`.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.API/Controllers/UserQuotaController.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T001`
- **Estimated effort:** 15 phút
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.API/CleanArchCqrs.API.csproj"
  ```
- **Scope category:** `none`
- **High-risk review route:** `Standard Review`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 2: Chuẩn Hóa Frontend API Client (driveApi.ts)

#### T003 [x] — Chuẩn Hóa driveApi.ts & Định Dạng DTO Từ Backend
- **Intent reference:** `SPEC.md` Section 3.1 & `PLAN.md` Section 5.2
- **Input and expected outcome:**
  - *Input:* `driveApi.ts` hiện tại chưa khớp cấu trúc dữ liệu `TenantStorageSummaryDto` từ `GET /api/files` và chưa có mapper cho `DriveItem`.
  - *Outcome:*
    1. Tạo hàm `mapFileEntryToDriveItem(dto)` để chuyển đổi `FileEntryDto` sang `DriveItem` chuẩn của frontend.
    2. Cập nhật `getDriveItems` đọc danh sách tệp từ `res.data?.value?.files || res.data?.files || []`.
    3. Cập nhật `uploadFiles(files, folderId)` gửi `FormData` với `Files` (`multipart/form-data`) lên `POST /api/files/upload`.
    4. Cập nhật `moveToTrash(id)` gọi `DELETE /api/files/{id}`.
    5. Cập nhật `deletePermanently(id)` gọi `DELETE /api/files/{id}/permanent`.
    6. Cập nhật `restoreFile(id)` gọi `POST /api/files/{id}/restore`.
    7. Thêm `userApi.getMyQuota()` gọi `GET /api/users/me/quota`.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/user/types/index.ts`
  - `Cloud-DMS-FE/src/features/user/api/driveApi.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T002`
- **Estimated effort:** 30 phút
- **Profile binding and exact verification command:**
  ```powershell
  npm run build --prefix "Cloud-DMS-FE"
  ```
- **Scope category:** `none`
- **High-risk review route:** `Standard Review`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 3: Tích Hợp Upload & Delete Vào UserPortal.tsx

#### T004 [x] — Thay Thế Mock Data Bằng Dữ Liệu Thực & Ghép Nối Xóa/Upload File
- **Intent reference:** `SPEC.md` REQ-UP-01, REQ-SD-01, REQ-PD-01 & `PLAN.md` Section 7 (Bước 3)
- **Input and expected outcome:**
  - *Input:* `UserPortal.tsx` đang dùng `INITIAL_DRIVE_ITEMS` tĩnh, các thao tác tải lên và xóa chỉ cập nhật state trong RAM.
  - *Outcome:*
    1. Thêm `useEffect` gọi `driveApi.getDriveItems()` khi component mount, lưu vào state `items` và cập nhật thông số dung lượng thực tế.
    2. Viết lại `handleUploadFiles` gọi `driveApi.uploadFiles(files, currentFolderId)`:
       - Hiển thị toast thông báo số lượng file upload thành công.
       - Cảnh báo nếu có file bị từ chối (virus, sai định dạng, vượt quota).
       - Tự động reload lại danh sách file và cập nhật dung lượng đã sử dụng.
    3. Viết lại `handleDeleteItem` gọi `driveApi.moveToTrash(item.id)`:
       - Di chuyển file vào thùng rác (`item.trashed = true`).
       - Hiển thị toast thành công.
    4. Bổ sung `handleRestoreFile` gọi `driveApi.restoreFile(item.id)` và `handlePermanentDelete` gọi `driveApi.deletePermanently(item.id)` khi thao tác trong thùng rác.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/user/UserPortal.tsx`
  - `Cloud-DMS-FE/src/features/user/views/DriveView.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T003`
- **Estimated effort:** 45 phút
- **Profile binding and exact verification command:**
  ```powershell
  npm run build --prefix "Cloud-DMS-FE"
  ```
- **Scope category:** `none`
- **High-risk review route:** `Standard Review`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 4: Tích Hợp Quota Thực Vào StorageView.tsx

#### T005 [x] — Nạp & Hiển Thị Hạn Mức Thực Tế Trong StorageView.tsx
- **Intent reference:** `SPEC.md` EARS-QT-01, 02 & `PLAN.md` Section 7 (Bước 4)
- **Input and expected outcome:**
  - *Input:* `StorageView.tsx` hiển thị giá trị tĩnh 1.0 TB.
  - *Outcome:*
    1. Gọi `userApi.getMyQuota()` hiển thị chính xác tổng dung lượng được cấp, dung lượng đã dùng, dung lượng còn lại.
    2. Đổi màu thanh tiến trình trực quan theo ngưỡng: xanh (<80%), vàng (80-95%), đỏ (>=95%).
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/user/views/StorageView.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T004`
- **Estimated effort:** 30 phút
- **Profile binding and exact verification command:**
  ```powershell
  npm run build --prefix "Cloud-DMS-FE"
  ```
- **Scope category:** `none`
- **High-risk review route:** `Standard Review`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

## 2. Traceability Matrix

| Yêu cầu nghiệp vụ | Mã Task | Tầng | Trạng thái |
| :--- | :--- | :--- | :--- |
| **EARS-QT-01** (Xem dung lượng cá nhân) | `T001`, `T002`, `T003`, `T005` | Backend & Frontend | Completed |
| **EARS-QT-02** (Cảnh báo ngưỡng 80%, 95%) | `T005` | Frontend UI | Completed |
| **REQ-UP-01** (Tải lên tệp tin) | `T003`, `T004` | Frontend Integration | Completed |
| **REQ-SD-01** (Xóa mềm tệp tin) | `T003`, `T004` | Frontend Integration | Completed |
| **REQ-PD-01** (Xóa vĩnh viễn tệp tin) | `T003`, `T004` | Frontend Integration | Completed |
| **REQ-RS-01** (Khôi phục tệp tin) | `T003`, `T004` | Frontend Integration | Completed |

---

## 3. Execution Sequencing
```text
T001 (Backend Query/DTO) [DONE]
   │
   ▼
T002 (Backend API Controller) [DONE]
   │
   ▼
T003 (Frontend API Client driveApi.ts) [DONE]
   │
   ▼
T004 (UserPortal: Upload/Delete Integration) [DONE]
   │
   ▼
T005 (StorageView: Quota Real Data) [DONE]
```

---

## 4. AI Agent Recommendation & Human Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Đề xuất danh sách 5 task T001..T005 cho Cổng Người Dùng để Human Reviewer xem xét và phê duyệt trước khi tiến hành thực thi.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt danh sách nhiệm vụ (TASKS.md) cho phân hệ Cổng Người Dùng.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED in the persisted review block.
```

### Human Final Review
- **Status:** `PENDING`

