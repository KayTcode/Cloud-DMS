# Feature Specification: File Preview Subsystem (SPEC.md)

- **Feature Slug:** `file-preview`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Risk Posture:** `MEDIUM`
- **Review Route:** `Architecture & Security Review Mandatory`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Context & Goal

### 1.1 Bối cảnh
Người dùng cần xem nhanh nội dung tài liệu trực tiếp trên trình duyệt mà không cần tải tệp tin về máy trạm. Do việc xử lý chuyển đổi xem trước (như PDF sang ảnh hoặc Word sang HTML/PDF) có thể tiêu tốn tài nguyên và thời gian, hệ thống tách làm 2 endpoint: kích hoạt yêu cầu (`POST /api/files/{id}/preview`) và truy vấn trạng thái bản xem trước (`GET /api/files/{id}/preview`).

### 1.2 Mục tiêu
- Cung cấp cơ chế tạo và lấy bản xem trước tệp tin an toàn theo quyền đọc `file.read`.
- Chuẩn hóa mã phản hồi HTTP phản ánh đúng trạng thái xử lý (`200`, `202`, `404`, `422`, `503`).
- Ngăn ngừa bộ nhớ đệm trình duyệt lưu trữ dữ liệu nhạy cảm bằng header `Cache-Control: no-store`.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn khi xem trước |
| :--- | :--- | :--- |
| **Người dùng có quyền đọc tệp tin** | `[Authorize(Policy = "file.read")]` | Yêu cầu và nhận kết quả xem trước cho tệp tin mình có quyền truy cập. |
| **Người dùng không có quyền hoặc tệp tin khác Tenant** | Không có quyền đọc | Bị chặn với mã HTTP `404 Not Found` (`unavailable`). |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận Yêu Cầu Kích Hoạt Xem Trước (Request Preview)
- **REQ-FP-01 (Event-driven):** `WHEN` client gửi yêu cầu `POST /api/files/{id}/preview`, hệ thống `SHALL` kiểm tra chính sách xác thực `[Authorize(Policy = "file.read")]`, áp dụng Rate Limiting `file-search` và thiết lập header `Cache-Control: no-store`.
- **REQ-FP-02 (Event-driven):** `WHEN` Use Case `RequestFilePreviewCommand` được thực thi, hệ thống `SHALL` gọi `IFileAccessService.CheckAsync` với quyền `PermissionNames.FileRead`.
- **REQ-FP-03 (Unwanted):** `IF` người dùng không có quyền đọc tệp tin hoặc tệp tin không tồn tại, `THEN` hệ thống `SHALL` trả về kết quả `unavailable` và ánh xạ sang HTTP `404 Not Found`.
- **REQ-FP-04 (Event-driven):** `WHEN` quyền đọc hợp lệ, hệ thống `SHALL` gọi `previews.GetAsync(context, grant, requestPreview: true, ct)` để đưa yêu cầu tạo bản xem trước vào hàng đợi xử lý.
- **REQ-FP-05 (Event-driven):** `WHEN` hoàn tất bước xử lý, hệ thống `SHALL` gọi `IFileAccessService.AssertStillValidAsync` để xác thực lại quyền trước khi trả kết quả cho client.

### 3.2 Truy Vấn Trạng Thái Bản Xem Trước (Get Preview)
- **REQ-FP-06 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files/{id}/preview`, hệ thống `SHALL` kiểm tra chính sách `[Authorize(Policy = "file.read")]` và thiết lập header `Cache-Control: no-store`.
- **REQ-FP-07 (Event-driven):** `WHEN` Use Case `GetFilePreviewQuery` được thực thi, hệ thống `SHALL` xác thực quyền đọc và gọi `previews.GetAsync(context, grant, requestPreview: false, ct)`.
- **REQ-FP-08 (Event-driven):** `WHEN` có kết quả từ `IPreviewProvider`, hệ thống `SHALL` ánh xạ mã HTTP tương ứng theo bảng trạng thái:
  - `Status == "ready"`: Trả về HTTP `200 OK` kèm thông tin liên kết xem trước.
  - `Status == "pending"`: Trả về HTTP `202 Accepted` thông báo bản xem trước đang được khởi tạo.
  - `Status == "unavailable"`: Trả về HTTP `404 Not Found`.
  - `Status == "unsupported"`: Trả về HTTP `422 Unprocessable Entity` thông báo định dạng tệp không được hỗ trợ.
  - `Status == "failed"`: Trả về HTTP `503 Service Unavailable`.

### 3.3 Phòng Ngừa Rò Rỉ Dữ Liệu & Race Conditions
- **REQ-FP-09 (Unwanted):** `IF` quyền truy cập bị thu hồi trong khi đang xử lý (TOCTOU), `THEN` hệ thống `SHALL` ném lỗi `AuthorizationChangedException` và trả về HTTP `409 Conflict` (`"AUTHORIZATION_CHANGED"`).
- **REQ-FP-10 (Unwanted):** `IF` request bị hủy giữa chừng hoặc quá hạn, `THEN` hệ thống `SHALL` trả về HTTP `499 Client Closed Request` hoặc `504 Gateway Timeout` (`"READ_CANCELLED"`).

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (No Cache Leakage):** Tiêu đề phản hồi bắt buộc có `Cache-Control: no-store` để tránh việc proxy hoặc trình duyệt lưu cache hình ảnh/nội dung tài liệu nhạy cảm.
- **NFR-RES-01 (Rate Limiting):** Áp dụng giới hạn tần suất theo chính sách `file-search` để ngăn chặn hành vi gửi liên tục yêu cầu render làm quá tải CPU.

---

## 5. Data Contracts

```csharp
public sealed record RequestFilePreviewCommand(Guid FileId) 
    : IRequest<PreviewResult>, ISensitiveReadRequest;

public sealed record GetFilePreviewQuery(Guid FileId) 
    : IRequest<PreviewResult>, ISensitiveReadRequest;

public sealed record PreviewResult(
    string Status, 
    long DocumentVersion, 
    string Reason, 
    Uri? PreviewUrl = null
);
```

### HTTP Status Code Mapping
| Trạng thái kết quả (`Status`) | HTTP Status Code | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- |
| `ready` | `200 OK` | Bản xem trước đã sẵn sàng để hiển thị. |
| `pending` | `202 Accepted` | Đang trong tiến trình tạo bản xem trước ngầm. |
| `unavailable` | `404 Not Found` | Tệp tin không tồn tại hoặc caller không có quyền xem. |
| `unsupported` | `422 Unprocessable Entity` | Định dạng tệp tin không nằm trong danh mục hỗ trợ tạo preview. |
| `failed` | `503 Service Unavailable` | Lỗi trong quá trình render tài liệu. |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả đầy đủ luồng kích hoạt tạo (`POST`) và truy vấn (`GET`) bản xem trước.
- [x] Định nghĩa tường minh các mã phản hồi HTTP trạng thái (`200`, `202`, `404`, `422`, `503`).
- [x] Đảm bảo tuân thủ kiểm tra quyền đọc 2 bước và chống lưu trữ cache ngoài ý muốn.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Đặc tả bám sát mã nguồn hiện có của `FilesController.RequestPreview` và `GetPreview`, đáp ứng kiến trúc phân tán Clean Architecture.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
