# Feature Specification: File Download Subsystem (SPEC.md)

- **Feature Slug:** `file-download`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Context & Goal

### 1.1 Bối cảnh
Người dùng cần tải tệp tin đã lưu trữ trên hệ thống Cloud-DMS về máy trạm. Tệp tin có thể nằm trên Local Storage, Google Drive hoặc OneDrive. Endpoint `GET /api/files/{id}/download` chịu trách nhiệm giải mã cấu hình, xác thực quyền truy cập và stream dữ liệu trực tiếp về client.

### 1.2 Mục tiêu
- Ngăn ngừa rò rỉ dữ liệu trái phép (IDOR/BOLA) bằng cơ chế kiểm tra quyền truy cập 3 tầng.
- Tối ưu hóa hiệu năng truyền dữ liệu dạng Stream, không làm tràn RAM của máy chủ API.
- Ẩn dấu sự tồn tại của file khi caller không có quyền truy cập (Fail-Closed).

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn khi tải xuống |
| :--- | :--- | :--- |
| **Employee (User)** | `file.read` | Chỉ tải các file thuộc phòng ban của mình và đang ở trạng thái hoạt động (`!IsDeleted`). |
| **DepartmentAdmin** | `file.read` | Tải các file thuộc phòng ban mình quản lý. |
| **TenantAdmin** | `file.read`, `file.*` | Tải bất kỳ tệp tin nào thuộc Tenant của mình. |
| **SystemAdmin** | Toàn quyền | Tải tệp tin trên toàn hệ thống phục vụ quản trị và hỗ trợ kỹ thuật. |
| **Unauthenticated** | Không có quyền | Bị chặn với mã HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực Yêu cầu
- **REQ-FD-01 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files/{id}/download`, hệ thống API `SHALL` kiểm tra JWT token và xác thực quyền `file.read`.
- **REQ-FD-02 (Unwanted):** `IF` request không có token hợp lệ hoặc thiếu quyền `file.read`, `THEN` hệ thống `SHALL` từ chối với HTTP `401 Unauthorized` hoặc `403 Forbidden`.

### 3.2 Kiểm tra Quyền Sở Hữu & Ranh Giới Dữ Liệu (Anti-IDOR)
- **REQ-FD-03 (Event-driven):** `WHEN` `DownloadFileQueryHandler` nhận yêu cầu với `FileId`, hệ thống `SHALL` truy vấn `FileEntry` kèm `StorageProvider` với điều kiện `f.Id == request.FileId && !f.IsDeleted`.
- **REQ-FD-04 (Unwanted):** `IF` tệp tin không tồn tại hoặc đã bị xóa mềm (`IsDeleted == true`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."`
- **REQ-FD-05 (Unwanted):** `IF` người gọi không phải `SystemAdmin` VÀ (`TenantId` của người gọi không khớp `fileEntry.TenantId`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."` (Fail-Closed, không tiết lộ mã lỗi 403 để tránh đoán ID).
- **REQ-FD-06 (Unwanted):** `IF` người gọi là `Employee` VÀ `fileEntry.DepartmentId` khác với `DepartmentId` của người gọi, `THEN` hệ thống `SHALL` từ chối tải xuống với thông báo: `"Tệp tin không tồn tại hoặc đã bị xóa."`

### 3.3 Streaming Dữ Liệu từ Cloud Storage Provider
- **REQ-FD-07 (Event-driven):** `WHEN` quyền truy cập được xác thực hợp lệ, hệ thống `SHALL` khởi tạo `IStorageService` tương ứng thông qua `IStorageServiceFactory.CreateService(fileEntry.StorageProvider)`.
- **REQ-FD-08 (Unwanted):** `IF` tệp tin không có thông tin `StorageProvider` gắn kèm trong CSDL, `THEN` hệ thống `SHALL` trả về lỗi: `"Không tìm thấy thông tin cấu hình nhà cung cấp lưu trữ của tệp tin."`
- **REQ-FD-09 (Event-driven):** `WHEN` `storageService.DownloadFileAsync(fileEntry.StorageKey)` được gọi, hệ thống `SHALL` mở stream nhị phân từ Provider và đóng gói vào `DownloadFileResult(Stream, FileName, ContentType)`.
- **REQ-FD-10 (Event-driven):** `WHEN` Controller nhận kết quả thành công, hệ thống `SHALL` trả về `File(result.Value.Stream, result.Value.ContentType, result.Value.FileName)` để client bắt đầu tải dữ liệu nhị phân.
- **REQ-FD-11 (Unwanted):** `IF` kết nối tới Cloud Storage Provider bị lỗi hoặc tệp tin vật lý không tồn tại trên Cloud, `THEN` hệ thống `SHALL` trả về HTTP `400 Bad Request` kèm thông báo: `"Không thể tải tệp tin từ kho lưu trữ: {ErrorMessage}"`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Anti-Enumeration):** Mọi trường hợp sai lệch Tenant hoặc phòng ban đều phải trả về cùng một thông điệp lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."`
- **NFR-PERF-01 (Memory Efficiency):** Dữ liệu truyền tải phải là luồng đọc trực tiếp (`Stream`), tuyệt đối không được đọc toàn bộ vào mảng `byte[]` hoặc RAM của ứng dụng.
- **NFR-SEC-02 (Credential Security):** Cấu hình Cloud Storage (`ConfigEncrypted`) chỉ được giải mã tạm thời trong bộ nhớ bằng AES-256 để khởi tạo `IStorageService`, không ghi ra log.

---

## 5. Data Contracts & Errors

### Request & Result DTO
```csharp
public record DownloadFileQuery(Guid FileId) : IRequest<Result<DownloadFileResult>>;

public record DownloadFileResult(
    Stream Stream,
    string FileName,
    string ContentType);
```

### Error Schema
| Tình huống | Mã lỗi phản hồi |
| :--- | :--- |
| Không tìm thấy / Bị xóa mềm / Sai Tenant / Sai Department | `"Tệp tin không tồn tại hoặc đã bị xóa."` |
| Thiếu cấu hình Storage Provider | `"Không tìm thấy thông tin cấu hình nhà cung cấp lưu trữ của tệp tin."` |
| Lỗi Cloud SDK | `"Không thể tải tệp tin từ kho lưu trữ: {Message}"` |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả đầy đủ các quy tắc EARS cho luồng kiểm tra quyền và streaming.
- [x] Đảm bảo nguyên tắc bảo mật Fail-Closed phòng chống IDOR.
- [x] Kiểm soát hiệu năng truyền tải stream trực tiếp từ Cloud Storage.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** SPEC đã bao quát hoàn toàn luồng tải xuống tệp tin, tuân thủ Clean Architecture và phòng ngừa rò rỉ dữ liệu.
- **Risk:** High (Truy xuất tệp tin nhị phân).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
