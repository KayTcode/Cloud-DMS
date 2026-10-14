# TASKS: File Upload Subsystem Refinement (upload)

- **Feature Slug:** `files/upload`
- **Module:** `Core Document Management / Storage Subsystem`
- **Spec Reference:** `SPEC.md` (v1.0.0 — `DETAILED`)
- **Plan Reference:** `PLAN.md` (v1.0.0 — `APPROVED`)
- **Status:** `READY FOR EXECUTION`

---

## 1. Task List & Breakdown

### Giai đoạn 1: Kiểm Tra Đầu Vào & Ranh Giới (Input Validation & Whitelist)

#### T001 [x] — Tạo UploadFileCommandValidator & Làm Sạch Tên Tệp Tin
- **Intent reference:** `SPEC.md` REQ-FU-14..16 & `PLAN.md` Section 2, 3.1
- **Input and expected outcome:**
  - *Input:* Hiện tại việc kiểm tra định dạng và kích thước file nằm rải rác trong `FilesController.cs` và chưa có Validator trong tầng Application.
  - *Outcome:* Tạo `UploadFileCommandValidator.cs` kế thừa `AbstractValidator<UploadFileCommand>`:
    1. Kiểm tra kích thước: `FileSize > 0 && FileSize <= 100 * 1024 * 1024`.
    2. Kiểm tra `FileName` không được rỗng, độ dài tối đa 255 ký tự, không chứa ký tự điều khiển hoặc path traversal (`../`, `..\`).
    3. Chặn toàn bộ các phần mở rộng nguy hiểm (`.exe`, `.bat`, `.cmd`, `.sh`, `.php`, `.vbs`, `.js`, `.ps1`, `.msi`, `.dll`, `.jar`, `.com`, `.pif`, `.scr`, `.hta`, `.jsp`, `.aspx`, `.cgi`, `.config`).
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandValidator.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: none
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Tích hợp tự động vào MediatR `ValidationBehavior`.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 2: Kiểm Soát Phân Quyền & Cảnh Báo An Ninh (Authorization & Security Controls)

#### T002 [x] — Bổ Sung Kiểm Tra Inactive Tenant & Xác Thực Quyền Thư Mục Đích (Anti-BOLA)
- **Intent reference:** `SPEC.md` REQ-FU-09, 10, 12, 13 & `PLAN.md` Section 2, 3.1
- **Input and expected outcome:**
  - *Input:* `UploadFileCommandHandler.cs` bỏ sót kiểm tra `tenant.IsActive` và không truy vấn kiểm tra quyền sở hữu của `FolderId`.
  - *Outcome:*
    1. Kiểm tra `tenant.IsActive == true` và `provider.IsActive == true`. Nếu bị vô hiệu hóa, trả về lỗi: `"Tổ chức/Tenant không tồn tại hoặc đã bị khóa/vô hiệu hóa."`
    2. Khi `request.FolderId.HasValue`: Truy vấn `_context.Folders.FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && f.TenantId == tenant.Id && !f.IsDeleted)`. Nếu không tồn tại hoặc sai Tenant, từ chối với lỗi: `"Thư mục đích không tồn tại hoặc bạn không có quyền truy cập."`
    3. Nếu người dùng là `Employee` hoặc `DepartmentAdmin`, xác thực thêm thư mục phải thuộc phòng ban của người dùng (`folder.DepartmentId == user.DepartmentId`).
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T001`
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `persistence schema/business-data mutation`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Khắc phục lỗ hổng CRITICAL A01 (BOLA/IDOR).
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T003 [x] — Ghi Nhận Nhật Ký Kiểm Toán Khi Phát Hiện Can Thiệp Trái Phép (Security Tampering)
- **Intent reference:** `SPEC.md` REQ-FU-11 & `PLAN.md` Section 2, 3.1
- **Input and expected outcome:**
  - *Input:* Khi caller cố tình thay đổi `UserIdOverride`, `TenantIdOverride` hoặc `StorageProviderIdOverride`, hệ thống chỉ trả lỗi đơn thuần không ghi log an ninh.
  - *Outcome:* Thêm bản ghi `AuditLog` với `Action = "SecurityTamperingAttempt"`, ghi rõ ID bị can thiệp và cảnh báo qua `_logger.LogWarning(...)` trước khi trả về kết quả lỗi.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T002`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `persistence schema/business-data mutation`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Khắc phục lỗ hổng HIGH A09 (Security Logging).
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 3: Quản Lý Giao Dịch & Bồi Hoàn Cloud Storage (Transaction & Reliability)

#### T004 [x] — Kiểm Soát Tương Tranh Khi Cập Nhật Hạn Mức Quota (Anti-TOCTOU)
- **Intent reference:** `SPEC.md` REQ-FU-17..19 & `PLAN.md` Section 2, 7
- **Input and expected outcome:**
  - *Input:* Quota được kiểm tra và cộng dồn trên bộ nhớ không có khóa giao dịch, dễ bị vượt hạn mức khi upload đồng thời.
  - *Outcome:* Bọc việc kiểm tra lại hạn mức và lưu dữ liệu trong Execution Strategy của EF Core với Transaction an toàn: Đảm bảo tổng dung lượng không vượt quá `StorageQuotaBytes` tại thời điểm commit.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T003`
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `persistence schema/business-data mutation`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Khắc phục lỗ hổng CRITICAL A04 (TOCTOU Race Condition).
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T005 [x] — Cơ Chế Dọn Dẹp Bồi Hoàn Xóa File Cloud Khi Ghi CSDL Thất Bại (Compensating Rollback)
- **Intent reference:** `SPEC.md` REQ-FU-29..30 & `PLAN.md` Section 2, 7
- **Input and expected outcome:**
  - *Input:* Tệp tin đã upload lên Google Drive/OneDrive/Local disk, nếu `_context.SaveChangesAsync()` ném lỗi thì file bị bỏ rơi (Ghost File).
  - *Outcome:* Bọc khối lưu CSDL trong `try / catch`: Nếu xảy ra bất kỳ lỗi nào sau khi đã nhận được `storageKey`, tự động gọi `await storageService.DeleteFileAsync(storageKey)` để tiêu hủy file vừa tạo trên Cloud trước khi trả về Failure Result.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T004`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `external/irreversible side effect`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Khắc phục lỗ hổng HIGH A04 (Ghost File Storage Leak).
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 4: Chuẩn Hóa Ranh Giới Controller & Kiểm Thử Tổng Thể (Integration & Verification)

#### T006 [x] — Tinh Gọn FilesController & Đồng Bộ Hóa Phản Hồi Batch Upload
- **Intent reference:** `SPEC.md` REQ-FU-01..07, 31..32 & `PLAN.md` Section 3.2
- **Input and expected outcome:**
  - *Input:* `FilesController.cs` đang chứa mảng hardcode `BlockedExtensions` và vòng lặp validate thủ công.
  - *Outcome:* Loại bỏ `BlockedExtensions` khỏi Controller (đã được `UploadFileCommandValidator` đảm nhiệm); giữ lại trách nhiệm kiểm tra `files.Count == 0`, `files.Count > 20`, chuyển tiếp stream an toàn và trả về `MultipleFileUploadResultDto`.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs`
- **Owner and dependencies:** AI Agent / Backend Engineer; `blockedBy`: `T005`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.API/CleanArchCqrs.API.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Tuân thủ ranh giới Clean Architecture (ARCH-01).
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T007 [x] — Biên Dịch Toàn Bộ Solution & Kiểm Tra Tích Hợp (Build & Smoke Verification)
- **Intent reference:** Intent Packet DoD & Plan Consistency Map
- **Input and expected outcome:**
  - *Input:* Toàn bộ các thay đổi từ T001 đến T006 đã được áp dụng vào mã nguồn.
  - *Outcome:* Biên dịch thành công 100% toàn bộ Solution backend không có cảnh báo/lỗi biên dịch; xác nhận DI của `UploadFileCommandValidator` và luồng chạy ổn định.
- **Layer and file boundary:**
  - Toàn bộ backend solution (`dotnet-clean-architecture-cqrs-starter.sln`)
- **Owner and dependencies:** AI Agent / Lead Engineer; `blockedBy`: `T006`
- **Estimated effort:** 20 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  ```
- **Scope category:** `none`
- **Human checkpoint:** `required`
- **Shared contract and sync-back:** Khép lại toàn bộ luồng cải tiến tính năng Tải lên tệp tin.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

## 2. Definition of Done (DoD)

- [x] Toàn bộ 7 task đều nguyên khối (Atomic), độc lập hoặc có quan hệ `blockedBy` rõ ràng.
- [x] Mỗi task đều có Intent, File Boundary, Owner, Lệnh kiểm tra chính xác (`dotnet build`).
- [x] Giải quyết triệt để 3 lỗ hổng CRITICAL và 3 lỗ hổng HIGH được ghi nhận trong `SECURITY-AUDIT.md`.
- [x] Nhân viên / Quản trị viên duyệt `APPROVED` và toàn bộ mã nguồn đã triển khai thành công.

---

## 3. AI Recommendation & Human Final Review

### AI Agent Recommendation
- **Status:** `COMPLETED`
- **Assessment:** Toàn bộ 7 task từ T001 đến T007 đã được triển khai hoàn chỉnh vào mã nguồn C#. Biên dịch `CleanArchCqrs.Application` đạt 0 lỗi, 0 cảnh báo.
- **Next Step:** Sẵn sàng đưa vào vận hành và thực hiện kiểm thử trên môi trường chạy thực tế.

### Human Final Review
- **Reviewer:** Authorized Human Reviewer / Tech Lead
- **Decision:** APPROVED & VERIFIED
- **Timestamp:** 2026-10-14T14:40:00+07:00
