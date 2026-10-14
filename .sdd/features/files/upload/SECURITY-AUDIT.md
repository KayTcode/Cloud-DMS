# API Security Audit Report: File Upload Subsystem

- **Feature Slug:** `file-upload`
- **Audit Target Files:**
  - `src/CleanArchCqrs.API/Controllers/FilesController.cs`
  - `src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommand.cs`
  - `src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs`
  - `src/CleanArchCqrs.Infrastructure/Services/ClamAvVirusScannerService.cs`
  - `src/CleanArchCqrs.Infrastructure/Services/Storage/LocalStorageService.cs`
  - `src/CleanArchCqrs.Infrastructure/Services/Storage/GoogleDriveStorageService.cs`
  - `src/CleanArchCqrs.API/appsettings.json`
- **Constitution Rules:** `CONSTITUTION.md` (`SEC-01`, `SEC-02`, `ARCH-01`, `ENG-02`), `AGENTS.md`
- **Methodology:** OWASP Top 10 API Security Checklist (A01 - A10) & Clean Architecture Boundaries

---

```text
API SECURITY AUDIT REPORT
Feature: file-upload | Profile: v1.0.0

CRITICAL:
  [A01] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:77,168,196 — Thiếu xác thực quyền sở hữu Folder (BOLA / Cross-Tenant Folder IDOR): Parameter `request.FolderId` được gán trực tiếp vào `FileEntry.FolderId` và chuyển sang `storageService.UploadFileAsync` mà không truy vấn CSDL để kiểm tra: (1) Thư mục có tồn tại không, (2) Thư mục có thuộc về `tenant.Id` của người dùng không, (3) Người dùng có quyền truy cập thư mục theo phân cấp phòng ban không. Kẻ tấn công có thể truyền GUID thư mục của Tenant khác để đính kèm file trái phép hoặc phá vỡ cấu trúc cây thư mục.
  [A04] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:86-100,211-231 — Điều kiện tương tranh Quota (TOCTOU Race Condition / Quota Bypass): Việc kiểm tra Quota (`StorageUsedBytes + FileSize > StorageQuotaBytes`) và cập nhật dung lượng diễn ra hoàn toàn trên bộ nhớ trong đối tượng Entity không có khóa dòng (Row-level lock), không có transaction cô lập `Serializable`, và không có Concurrency Token (`[ConcurrencyCheck]` / RowVersion). Khi một hoặc nhiều người dùng gửi nhiều file dung lượng lớn đồng thời, tất cả các luồng đều vượt qua bước kiểm tra Quota trước khi commit vào DB, dẫn đến việc Tenant và User có thể xài vượt quá dung lượng lưu trữ cho phép.
  [A01] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:75-82 — Bỏ sót kiểm tra trạng thái kích hoạt của Tổ chức (Inactive Tenant Bypass): Lệnh truy vấn `_context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId.Value)` không kiểm tra điều kiện `t.IsActive`. Nếu một Tenant bị Quản trị viên vô hiệu hóa, tài khoản thuộc Tenant đó vẫn có thể thực hiện tải lên tệp tin và tiêu tốn dung lượng Cloud Storage.

HIGH:
  [A04] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:175-186,247 — Tệp tin mồ côi chiếm dung lượng Cloud (Orphan File Leak / Missing Compensation Transaction): Luồng xử lý tải file lên Cloud Provider trước (`storageService.UploadFileAsync`), sau đó mới gọi `_context.SaveChangesAsync()`. Nếu lệnh lưu CSDL gặp lỗi (DB Timeout, vi phạm Unique Constraint của `StorageKey`, mất kết nối mạng), tệp tin đã nằm vĩnh viễn trên Google Drive/OneDrive/Local Disk mà không có bản ghi CSDL quản lý và không có cơ chế tự động dọn dẹp bồi hoàn (Rollback Cleanup).
  [A03] src/CleanArchCqrs.API/Controllers/FilesController.cs:21-24,56-66 — Lọc định dạng bằng danh sách đen tại tầng Interface (Extension Blacklist & Clean Architecture Violation): `BlockedExtensions` được hardcode tại API Controller thay vì Domain/Application layer. Cơ chế danh sách đen (Blacklist) bỏ sót nhiều định dạng nguy hiểm (ví dụ: `.jsp`, `.aspx`, `.svg` chứa mã độc XSS dạng XML kịch bản, `.shtml`, `.config`). Hệ thống hoàn toàn tin tưởng `ContentType` do trình duyệt gửi lên mà không kiểm tra Magic Bytes (chữ ký nhị phân).
  [A09] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:42-45,63-71 — Bỏ qua ghi nhật ký bảo mật khi phát hiện hành vi can thiệp trái phép (Silent Tampering without Security Audit Log): Khi phát hiện `UserIdOverride`, `TenantIdOverride`, hoặc `StorageProviderIdOverride` bị giả mạo, Handler chỉ trả về kết quả lỗi đơn thuần mà không ghi `AuditLog` cảnh báo bảo mật. Quản trị viên hệ thống không thể giám sát các nỗ lực tấn công leo quyền hoặc thay đổi tổ chức của kẻ xấu.

CONFIGURATION GAP:
  [SEC-01] src/CleanArchCqrs.API/appsettings.json:14,35,40 — Lộ bí mật hệ thống trong tệp cấu hình nguồn (Hardcoded Secrets): Chuỗi `JwtSettings:SecretKey`, mật khẩu tài khoản email SMTP `EmailSettings:Password` (`laiw yryo opsr oist`), và `GoogleAuth:ClientSecret` được lưu dưới dạng văn bản thuần (Plaintext) trong file `appsettings.json`. Vi phạm trực tiếp quy tắc `SEC-01` của Constitution. Cần chuyển sang cơ chế biến môi trường (Environment Variables) hoặc Azure Key Vault / User Secrets.
  [ARCH-01] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommand.cs — Thiếu Validation Pipeline Behavior (FluentValidation): Luồng Upload đang tự validate thủ công phân tán giữa Controller và Handler, chưa có `UploadFileCommandValidator` chuẩn hóa tích hợp vào MediatR Pipeline.

PASSED:
  [A01] src/CleanArchCqrs.API/Controllers/FilesController.cs:29 — Thực thi phân quyền endpoint thông qua chính sách `[Authorize(Policy = "file.upload")]`.
  [A04] src/CleanArchCqrs.API/Controllers/FilesController.cs:30,32 — Áp dụng kiểm soát tài nguyên: Giới hạn tần suất gọi `[EnableRateLimiting("upload")]` và giới hạn kích thước tối đa 100MB `[RequestSizeLimit(100 * 1024 * 1024)]`.
  [A07] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:36-55 — Trích xuất danh tính và kiểm tra trạng thái kích hoạt của người dùng (`user.IsActive`) từ `ICurrentUserService`.
  [A08] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:124-147 & src/CleanArchCqrs.Infrastructure/Services/ClamAvVirusScannerService.cs:40-64 — Tích hợp quét mã độc trực tiếp trên stream bằng ClamAV TCP Stream protocol. Thực thi nghiêm ngặt cơ chế Fail-Closed trên môi trường Non-Development khi ClamAV không khả dụng.
  [A02] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:155-158 — Tính toán mã băm SHA-256 an toàn từ stream dữ liệu, phục vụ kiểm tra toàn vẹn và chống trùng lặp.
  [A02] src/CleanArchCqrs.Infrastructure/Services/Storage/StorageServiceFactory.cs:24 — Thông tin đăng nhập của Cloud Storage Provider (`ConfigEncrypted`) được mã hóa và giải mã bằng thuật toán AES thông qua `IAesEncryptionService`.
  [A03] src/CleanArchCqrs.Infrastructure/Services/Storage/LocalStorageService.cs:26,48 — Phòng chống tấn công duyệt thư mục (Path Traversal Defense) trên Local Storage bằng cách kiểm tra `fullFilePath.StartsWith(fullBasePath)` và chuẩn hóa tên file với `Path.GetInvalidFileNameChars()`.
  [A09] src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:133-145,234-246 — Ghi nhận `AuditLog` chi tiết khi tải lên thành công và khi phát hiện mã độc từ ClamAV.

REMEDIATION:
  - Binding: Cần phê duyệt cập nhật luồng xử lý `UploadFileCommandHandler` và bổ sung cấu hình bí mật môi trường.
  - Change:
    1. [A01 Fix] Trong `UploadFileCommandHandler`: Thêm bước kiểm tra `tenant.IsActive == true`. Nếu `request.FolderId.HasValue`, truy vấn `_context.Folders.FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && f.TenantId == tenant.Id)` và kiểm tra phân quyền phòng ban. Nếu vi phạm, từ chối tải lên ngay lập tức.
    2. [A04 Fix] Trong `UploadFileCommandHandler`: Bọc toàn bộ quá trình cập nhật dung lượng và lưu CSDL trong giao dịch có kiểm soát tương tranh (hoặc bổ sung `RowVersion` / Concurrency Token cho `Tenant`, `Department`, `UserStorageQuota` và bắt lỗi `DbUpdateConcurrencyException`).
    3. [A04 Fix] Bọc khối lưu CSDL trong `try / catch`: Nếu `_context.SaveChangesAsync()` ném ngoại lệ, gọi `await storageService.DeleteFileAsync(storageKey)` để xóa file vừa tải lên Cloud Provider trước khi trả về lỗi cho người dùng.
    4. [A03 Fix] Di chuyển định nghĩa danh sách phần mở rộng từ Controller vào `UploadFileCommandValidator` trong tầng Application. Chuyển sang mô hình Whitelist (chỉ cho phép các định dạng tài liệu thông dụng) và bổ sung kiểm tra chữ ký nhị phân (Magic Bytes).
    5. [A09 Fix] Khi phát hiện `UserIdOverride` hoặc `TenantIdOverride` không khớp danh tính token, ghi ngay bản ghi `AuditLog` cảnh báo an ninh với `Action = "SecurityTamperingAttempt"`.
    6. [SEC-01 Fix] Xóa sạch các chuỗi secret trong `appsettings.json`, chuyển sang dùng `.env` hoặc hệ thống quản lý bí mật môi trường.
  - Verification: N/A — Cần Human Reviewer duyệt remediation plan trước khi áp dụng vào mã nguồn.
  - Spec/Plan impact: `.sdd/features/file-upload/SPEC.md` đã được khởi tạo và ghi nhận toàn bộ các yêu cầu bảo mật trên.
```

---

## AI Agent Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Quá trình kiểm toán bảo mật đã phát hiện 3 lỗ hổng CRITICAL (BOLA/IDOR trên FolderId, Race condition trên Quota, và bỏ sót kiểm tra Inactive Tenant), cùng với các lỗ hổng HIGH về Ghost file trên Cloud, Hardcoded Secrets và thiếu Audit Log cho hành vi giả mạo ID. Toàn bộ các phát hiện đã được ánh xạ vào tài liệu đặc tả `SPEC.md`.
- **Residual Risk:** Cao nếu chưa áp dụng các bản vá sửa đổi cho `FolderId` authorization và cơ chế bồi hoàn xóa file Cloud khi DB commit thất bại.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
