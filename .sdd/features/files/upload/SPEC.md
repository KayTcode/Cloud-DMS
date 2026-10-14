# Feature Specification: File Upload Subsystem (SPEC.md)

- **Feature Slug:** `file-upload`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Context & Goal

### 1.1 Bối cảnh (Context)
Hệ thống Cloud-DMS quản lý tệp tin đa người thuê (Multi-Tenancy) với kiến trúc Clean Architecture & CQRS. Phân hệ tải lên tệp tin (`POST /api/files/upload`) là cửa ngõ chính tiếp nhận dữ liệu nhị phân từ người dùng cuối, đồng thời tương tác với dịch vụ quét mã độc (ClamAV TCP Stream), dịch vụ mã hóa AES-256, và hệ thống lưu trữ đa đám mây (Local Disk, Google Drive API v3, Microsoft Graph OneDrive API).

### 1.2 Mục tiêu (Goal)
- Định nghĩa chi tiết và chuẩn hóa quy trình xử lý tệp tin từ tầng giao diện HTTP (`FilesController`) tới tầng ứng dụng (`UploadFileCommandHandler`) và tầng hạ tầng lưu trữ.
- Đảm bảo tính toàn vẹn dữ liệu, chống tấn công qua tệp tin độc hại, ngăn ngừa rò rỉ dữ liệu hoặc leo quyền chéo tổ chức (Anti-BOLA/IDOR), và loại bỏ hoàn toàn các điều kiện tương tranh (Race Conditions) gây thất thoát hạn mức Quota.
- Cung cấp hợp đồng dữ liệu chuẩn xác (DTOs), mã lỗi định kiểu an toàn (Typed Errors) và quy tắc bồi hoàn giao dịch khi gặp sự cố lưu trữ.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permissions | Quyền hạn trong luồng Upload |
| :--- | :--- | :--- |
| **Employee (User)** | `file.upload`, `file.read` | Tải lên tệp tin cá nhân hoặc tệp tin thuộc phòng ban của mình. Bị giới hạn bởi Quota cá nhân, Quota phòng ban và Quota Tenant. |
| **DepartmentAdmin** | `file.upload`, `file.read`, `storage.manage` | Tải lên tệp tin vào bất kỳ thư mục nào thuộc phạm vi phòng ban quản lý. |
| **TenantAdmin** | `file.upload`, `file.read`, `file.*`, `storage.manage` | Tải lên tệp tin trên toàn tổ chức (Tenant). |
| **SystemAdmin** | Toàn quyền hệ thống | Quản trị và cấu hình Storage Provider, có thể kiểm tra mọi luồng tải lên. |
| **Unauthenticated / Unknown** | Không có quyền | Bị chặn ngay tại API Gateway / Middleware với mã HTTP `401 Unauthorized` hoặc `403 Forbidden`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Giao diện & Ranh giới Tiếp nhận HTTP (Interface Boundary)

- **REQ-FU-01 (Event-driven):** `WHEN` người dùng gửi yêu cầu HTTP `POST /api/files/upload` kèm theo danh sách tệp tin dạng `multipart/form-data`, hệ thống API `SHALL` kiểm tra JWT token và xác thực quyền `file.upload`.
- **REQ-FU-02 (Unwanted):** `IF` yêu cầu không có header xác thực hoặc token không hợp lệ/hết hạn, `THEN` hệ thống `SHALL` từ chối với HTTP `401 Unauthorized`.
- **REQ-FU-03 (Unwanted):** `IF` tài khoản người dùng không có claim/permission `file.upload`, `THEN` hệ thống `SHALL` từ chối với HTTP `403 Forbidden`.
- **REQ-FU-04 (Unwanted):** `IF` tổng kích thước payload vượt quá 100 MB (`RequestSizeLimit`), `THEN` web server `SHALL` ngắt kết nối và trả về mã HTTP `413 Payload Too Large`.
- **REQ-FU-05 (Unwanted):** `IF` danh sách tệp tin đính kèm rỗng (`files == null || files.Count == 0`), `THEN` hệ thống `SHALL` trả về HTTP `400 Bad Request` kèm thông báo lỗi: `"Không tìm thấy tệp tin đính kèm để tải lên."`
- **REQ-FU-06 (Unwanted):** `IF` số lượng tệp tin tải lên trong một request vượt quá 20 tệp (`files.Count > 20`), `THEN` hệ thống `SHALL` từ chối yêu cầu với HTTP `400 Bad Request` kèm thông báo: `"Mỗi lần tải lên chỉ được phép gửi tối đa 20 tệp tin."`
- **REQ-FU-07 (Ubiquitous):** Hệ thống `SHALL` áp dụng rate limiting policy `"upload"` cho endpoint `POST /api/files/upload` để chống tấn công từ chối dịch vụ (DoS / Flooding).

---

### 3.2 Kiểm tra Danh tính, Tổ chức & Thư mục Đích (Authorization & Multi-Tenancy)

- **REQ-FU-08 (Event-driven):** `WHEN` `UploadFileCommandHandler` nhận `UploadFileCommand`, hệ thống `SHALL` trích xuất `UserId` và `TenantId` trực tiếp từ `ICurrentUserService`.
- **REQ-FU-09 (Unwanted):** `IF` `currentUserId` không tồn tại hoặc người dùng không có trong CSDL hoặc `user.IsActive == false`, `THEN` hệ thống `SHALL` trả về lỗi: `"Người dùng hiện tại không hợp lệ hoặc đã bị vô hiệu hóa."`
- **REQ-FU-10 (Unwanted):** `IF` `currentTenantId` không tồn tại hoặc tổ chức tương ứng có `tenant.IsActive == false`, `THEN` hệ thống `SHALL` trả về lỗi: `"Tổ chức/Tenant không tồn tại hoặc đã bị khóa/vô hiệu hóa."`
- **REQ-FU-11 (Unwanted):** `IF` command chứa `UserIdOverride` khác với `currentUserId`, hoặc `TenantIdOverride` khác với `currentTenantId`, hoặc `StorageProviderIdOverride` trái phép, `THEN` hệ thống `SHALL`:
  1. Ghi ngay bản ghi cảnh báo an ninh vào `AuditLog` với `Action = "SecurityTamperingAttempt"`.
  2. Từ chối yêu cầu với lỗi: `"Phát hiện hành vi thay đổi danh tính/tổ chức trái phép."`
- **REQ-FU-12 (Event-driven):** `WHEN` yêu cầu tải lên có truyền `FolderId` (`request.FolderId.HasValue`), hệ thống `SHALL` truy vấn bản ghi `Folder` trong CSDL.
- **REQ-FU-13 (Unwanted):** `IF` thư mục đích không tồn tại, hoặc `folder.TenantId != tenant.Id`, hoặc thư mục thuộc phòng ban khác mà người dùng không có quyền truy cập, `THEN` hệ thống `SHALL` từ chối tải lên với lỗi: `"Thư mục đích không tồn tại hoặc bạn không có quyền truy cập."` (Chống lỗ hổng BOLA/IDOR).

---

### 3.3 Kiểm tra Định dạng & Tính Hợp Lệ của Tệp Tin (File Validation)

- **REQ-FU-14 (Unwanted):** `IF` tệp tin có kích thước bằng 0 (`item.Length == 0`), `THEN` hệ thống `SHALL` bỏ qua hoặc ghi nhận lỗi tệp tin rỗng vào danh sách `Failed`.
- **REQ-FU-15 (Unwanted):** `IF` phần mở rộng của tệp tin thuộc danh sách cấm (`.exe`, `.bat`, `.cmd`, `.sh`, `.php`, `.vbs`, `.js`, `.ps1`, `.msi`, `.dll`, `.jar`, `.com`, `.pif`, `.scr`, `.hta`, `.jsp`, `.aspx`, `.cgi`, `.config`) HOẶC không thuộc danh sách loại tệp được cấu hình cho Tenant, `THEN` hệ thống `SHALL` từ chối tệp tin và thêm vào danh sách `Failed` với thông báo: `"Tệp tin '{FileName}' có định dạng không được phép tải lên hệ thống vì lý do bảo mật."`
- **REQ-FU-16 (Ubiquitous):** Hệ thống `SHALL` làm sạch tên tệp tin (`FileName`), loại bỏ ký tự điều khiển, ký tự nguy hiểm (`../`, `..\`) và mã độc kịch bản (XSS payload) trước khi lưu vào CSDL và Cloud Storage.

---

### 3.4 Kiểm soát Hạn Mức Dung Lượng (3-Tier Quota & Concurrency Safety)

- **REQ-FU-17 (Event-driven):** `WHEN` kiểm tra dung lượng cho tệp tin có kích thước `request.FileSize`, hệ thống `SHALL` kiểm tra tuần tự:
  1. Hạn mức Tenant: `tenant.StorageUsedBytes + request.FileSize <= tenant.StorageQuotaBytes`.
  2. Hạn mức User: `userQuota == null || userQuota.UsedBytes + request.FileSize <= userQuota.QuotaBytes`.
  3. Dung lượng Cloud Provider: `provider.TotalCapacityBytes == 0 || provider.UsedCapacityBytes + request.FileSize <= provider.TotalCapacityBytes`.
- **REQ-FU-18 (Unwanted):** `IF` bất kỳ hạn mức nào bị vượt quá, `THEN` hệ thống `SHALL` từ chối tải lên và ghi nhận lỗi vào danh sách `Failed` với chi tiết dung lượng cụ thể.
- **REQ-FU-19 (Ubiquitous):** Hệ thống `SHALL` thực hiện cập nhật dung lượng `StorageUsedBytes` và lưu `FileEntry` trong môi trường bảo đảm tính nhất quán (Row Version / Concurrency Token / Database Transaction) để ngăn chặn tình trạng thất thoát Quota do race condition khi nhiều tệp tin được tải lên đồng thời.

---

### 3.5 Quét Mã Độc Thời Gian Thực (ClamAV TCP Stream & Fail-Closed)

- **REQ-FU-20 (Event-driven):** `WHEN` bắt đầu kiểm tra nội dung tệp tin, hệ thống `SHALL` truyền trực tiếp `FileStream` qua socket TCP tới ClamAV daemon bằng giao thức `zINSTREAM`.
- **REQ-FU-21 (Unwanted):** `IF` ClamAV phản hồi phát hiện mã độc (`scanResult.IsInfected == true`), `THEN` hệ thống `SHALL`:
  1. Hủy ngay lập tức luồng tải lên của tệp tin đó.
  2. Ghi bản ghi `AuditLog` với `Action = "VirusDetected"`, kèm tên mối đe dọa `ThreatName` và `FileName`.
  3. Thêm tệp tin vào danh sách `Failed` với thông báo: `"Phát hiện mã độc trong tệp tin: {ThreatName}! Quá trình tải lên đã bị chặn."`
- **REQ-FU-22 (State-driven):** `WHILE` hệ thống đang vận hành trên môi trường Non-Development (Staging / Production), `IF` ClamAV daemon bị mất kết nối, quá hạn timeout hoặc bị vô hiệu hóa trong cấu hình, `THEN` hệ thống `SHALL` thực thi chính sách **Fail-Closed** (từ chối tải lên vì lý do an toàn bảo mật, cấm không cho phép bypass).
- **REQ-FU-23 (State-driven):** `WHILE` hệ thống đang vận hành trên môi trường Development (`IHostEnvironment.IsDevelopment()`), `IF` ClamAV không khả dụng, hệ thống `SHALL` ghi log cảnh báo và cho phép đi tiếp nhằm phục vụ thử nghiệm cục bộ.

---

### 3.6 Băm Toàn Vẹn & Lưu Trữ Đa Đám Mây (SHA-256 & Multi-Cloud Routing)

- **REQ-FU-24 (Ubiquitous):** Hệ thống `SHALL` tính toán mã băm SHA-256 từ luồng nhị phân của tệp tin và lưu trữ chuỗi Hex 64 ký tự vào thuộc tính `FileEntry.Hash`.
- **REQ-FU-25 (Event-driven):** `WHEN` các bước kiểm tra hợp lệ, hệ thống `SHALL` khởi tạo `IStorageService` qua `IStorageServiceFactory` dựa trên nhà cung cấp lưu trữ của Tenant (`tenant.StorageProvider`).
- **REQ-FU-26 (Ubiquitous):** Thông tin cấu hình nhạy cảm của Cloud Storage Provider (`ConfigEncrypted`) `SHALL` được giải mã bằng `IAesEncryptionService` (AES-256) trong bộ nhớ, không được log ra file hoặc console.
- **REQ-FU-27 (Event-driven):** `WHEN` tải lên Local Disk, hệ thống `SHALL` tạo khóa lưu trữ an toàn chống Path Traversal (`{Guid}_{SafeFileName}`) và kiểm tra đường dẫn đích nằm hoàn toàn trong thư mục cơ sở (`_basePath`).
- **REQ-FU-28 (Event-driven):** `WHEN` tải lên Google Drive hoặc OneDrive, hệ thống `SHALL` truyền stream tệp tin lên API chính thống qua SDK tương ứng và nhận về `StorageKey` đại diện cho ID tệp tin trên đám mây.

---

### 3.7 Nguyên Tắc Nguyên Khối & Bồi Hoàn (Atomic Persistence & Compensation)

- **REQ-FU-29 (Event-driven):** `WHEN` lưu trữ trên Cloud hoàn tất, hệ thống `SHALL` tạo bản ghi `FileEntry`, cộng dung lượng sử dụng cho `Tenant`, `Department`, `StorageProvider`, `UserStorageQuota`, tạo `AuditLog` với `Action = "Upload"` và gọi `_context.SaveChangesAsync()`.
- **REQ-FU-30 (Unwanted):** `IF` quá trình ghi vào CSDL thất bại sau khi tệp tin đã được lưu lên Cloud Provider, `THEN` hệ thống `SHALL` kích hoạt cơ chế bồi hoàn (Compensating Transaction): gọi `storageService.DeleteFileAsync(storageKey)` để dọn dẹp file vật lý, ngăn chặn hoàn toàn tệp tin mồ côi (Ghost / Orphan Files).

---

### 3.8 Xử Lý Tải Lên Hàng Loạt (Batch Upload Isolation)

- **REQ-FU-31 (Ubiquitous):** Trong một lượt gửi tải lên nhiều tệp tin (`List<IFormFile>`), nếu một tệp tin bị thất bại (do virus, do sai định dạng hoặc do dung lượng), hệ thống `SHALL NOT` làm gián đoạn hoặc hủy bỏ các tệp tin hợp lệ khác trong cùng lô tải lên.
- **REQ-FU-32 (Event-driven):** `WHEN` toàn bộ danh sách tệp tin được xử lý xong, API `SHALL` trả về mã HTTP `200 OK` kèm theo DTO `MultipleFileUploadResultDto` chứa danh sách đầy đủ các tệp tin thành công (`Uploaded`) và các tệp tin thất bại kèm lý do cụ thể (`Failed`).

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Security - Zero Secrets):** Không hardcode JWT Secret, mật khẩu SMTP hay OAuth Client Secrets trong file cấu hình git-tracked. Mọi bí mật phải nạp qua Environment Variables.
- **NFR-SEC-02 (Security - Anti-IDOR):** Mọi tài nguyên (`FolderId`, `TenantId`, `UserId`) đều phải được kiểm tra ràng buộc sở hữu trước khi thực hiện thao tác.
- **NFR-PERF-01 (Performance):** Stream dữ liệu tệp tin không được nạp toàn bộ vào RAM dưới dạng `byte[]` mà phải đọc/ghi theo chunk stream (buffer 8192 bytes) để đảm bảo bộ nhớ máy chủ ổn định khi nhiều người dùng tải file đồng thời.
- **NFR-REL-01 (Reliability):** Thời gian chờ kết nối ClamAV daemon tối đa 3 giây; nếu quá hạn, chuyển sang xử lý lỗi Fail-Closed hoặc Fail-Safe tùy môi trường.

---

## 5. Data Contracts & Typed Errors

### 5.1 Command & DTOs
```csharp
public record UploadFileCommand : IRequest<Result<FileEntryDto>>
{
    public required Stream FileStream { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public Guid? FolderId { get; init; }
    public Guid? TenantIdOverride { get; init; }
    public Guid? UserIdOverride { get; init; }
    public Guid? StorageProviderIdOverride { get; init; }
}

public class MultipleFileUploadResultDto
{
    public List<FileEntryDto> Uploaded { get; set; } = new();
    public List<FileUploadFailureDto> Failed { get; set; } = new();
}

public class FileUploadFailureDto
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Error { get; set; } = string.Empty;
}
```

### 5.2 Error Codes & Standard Messages
| Error Code | Mô tả | HTTP Mapping |
| :--- | :--- | :--- |
| `NO_FILES_ATTACHED` | Không tìm thấy tệp tin đính kèm | 400 Bad Request |
| `BATCH_SIZE_EXCEEDED` | Vượt quá 20 tệp tin trên một lượt gửi | 400 Bad Request |
| `FILE_SIZE_EXCEEDED` | Tệp tin vượt quá 100 MB | 413 Payload Too Large |
| `INVALID_EXTENSION` | Tệp tin có phần mở rộng bị cấm | Ghi nhận vào DTO `Failed` |
| `UNAUTHORIZED_FOLDER` | Thư mục đích không tồn tại hoặc không thuộc quyền sở hữu | Ghi nhận vào DTO `Failed` |
| `QUOTA_EXCEEDED` | Hết dung lượng Tenant, User hoặc Cloud Provider | Ghi nhận vào DTO `Failed` |
| `VIRUS_DETECTED` | Phát hiện mã độc trong tệp tin (ClamAV) | Ghi nhận vào DTO `Failed` |
| `SCAN_UNAVAILABLE` | Dịch vụ quét virus không phản hồi (Fail-Closed) | Ghi nhận vào DTO `Failed` |
| `TAMPERING_DETECTED` | Phát hiện thay đổi Tenant/User/Provider ID bất hợp pháp | Ghi nhận vào DTO `Failed` / 400 |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả đầy đủ các yêu cầu chức năng theo cú pháp chuẩn EARS.
- [x] Phân định ranh giới Clean Architecture rõ ràng giữa Controller, Command Handler, Services và Database.
- [x] Thiết lập đầy đủ các Invariants bảo mật: Anti-BOLA cho `FolderId`, kiểm soát Quota 3 tầng, ClamAV fail-closed, SHA-256 integrity, và cơ chế dọn dẹp bồi hoàn file rác trên Cloud.
- [x] Xác định rõ ràng hợp đồng lỗi (Typed Error Schema) và DTO phản hồi.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Đặc tả kỹ thuật (SPEC.md) đã bao quát toàn bộ các luồng chức năng và xử lý ngoại lệ theo chuẩn EARS, Clean Architecture và OWASP. Tài liệu đã phản ánh đầy đủ các phát hiện bảo mật từ quá trình kiểm toán.
- **Risk:** High (Core subsystem handling untrusted binary streams).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
