# Feature Specification: Direct Read Access & Presigned URL Subsystem (SPEC.md)

- **Feature Slug:** `file-read-access`
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
Nhằm tối ưu hóa băng thông cho máy chủ Web API và hỗ trợ tốt các tác vụ truyền tải tệp tin dung lượng lớn hoặc phát trực tuyến (Media Streaming), hệ thống cung cấp endpoint `GET /api/files/{id}/read-access` để tạo đường dẫn truy cập trực tiếp có chữ ký số (Presigned Read URL) từ nhà cung cấp Object Storage (S3).

### 1.2 Mục tiêu
- Cấp URL đọc có thời hạn cho 3 mục đích: tải về (`download`), hiển thị trực tiếp (`inline`), và truyền phát (`stream`).
- Áp dụng danh sách trắng MIME types để phòng chống tấn công chèn mã XSS.
- Ghi nhận nhật ký kiểm toán (Audit Log) bắt buộc cho mọi hành vi cấp quyền đọc.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn khi lấy URL đọc |
| :--- | :--- | :--- |
| **Người dùng có quyền đọc tài liệu** | `[Authorize(Policy = "file.read")]` | Nhận Presigned Read URL cho tệp tin mình có quyền. |
| **Người dùng không có quyền hoặc sai Tenant** | Không có quyền | Bị từ chối với thông báo: `"Không thể đọc tệp tin."` |
| **Unauthenticated** | Không có quyền | Bị chặn với mã HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Kiểm tra Tham số
- **REQ-RA-01 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files/{id}/read-access` kèm tham số `purpose` (mặc định là `"download"`), hệ thống `SHALL` kiểm tra chính sách `[Authorize(Policy = "file.read")]` và thiết lập header `Cache-Control: no-store`.
- **REQ-RA-02 (Unwanted):** `IF` tham số `purpose` không nằm trong danh sách `["download", "inline", "stream"]`, `THEN` hệ thống `SHALL` trả về lỗi: `"Mục đích đọc không hợp lệ."`
- **REQ-RA-03 (Unwanted):** `IF` `purpose == "inline"` nhưng cấu hình hệ thống thiếu nguồn gốc công khai (`options.PublicApiOrigin == null`), `THEN` hệ thống `SHALL` trả về lỗi: `"Chưa cấu hình origin API công khai."`

### 3.2 Xác Thực Quyền Đọc & Kiểm Tra Trạng Thái Tệp Tin
- **REQ-RA-04 (Event-driven):** `WHEN` Use Case `GetFileReadAccessQuery` thực thi, hệ thống `SHALL` gọi `IFileAccessService.CheckAsync` với quyền `PermissionNames.FileRead`.
- **REQ-RA-05 (Unwanted):** `IF` quyền đọc không hợp lệ hoặc không tìm thấy tệp tin đang hoạt động (`FileLifecycle.Active` và `!IsDeleted`), `THEN` hệ thống `SHALL` trả về lỗi: `"Không thể đọc tệp tin."`
- **REQ-RA-06 (Ubiquitous):** Tệp tin chỉ hợp lệ khi có phiên bản nội dung `FileContentVersion` sạch mã độc (`ScanStatus == FileScanStatus.Clean`), có khóa đối tượng `ObjectKey` không rỗng và nhà cung cấp lưu trữ đang kích hoạt (`provider.IsActive == true`).

### 3.3 Thẩm Định Định Dạng An Toàn & Loại Hình Kho Lưu Trữ
- **REQ-RA-07 (Unwanted):** `IF` `purpose == "inline"` VÀ `ContentType` không thuộc danh sách an toàn (`application/pdf`, `image/png`, `image/jpeg`, `image/gif`, `image/webp`, `text/plain`), `THEN` hệ thống `SHALL` từ chối với lỗi: `"Định dạng không hỗ trợ mục đích đọc này."`
- **REQ-RA-08 (Unwanted):** `IF` `purpose == "stream"` VÀ `ContentType` không thuộc danh sách đa phương tiện (`audio/mpeg`, `audio/mp4`, `audio/ogg`, `audio/wav`, `video/mp4`, `video/webm`), `THEN` hệ thống `SHALL` từ chối với lỗi: `"Định dạng không hỗ trợ mục đích đọc này."`
- **REQ-RA-09 (Unwanted):** `IF` nhà cung cấp lưu trữ của tệp tin không phải loại S3 (`provider.Type != StorageProviderType.S3`), `THEN` hệ thống `SHALL` từ chối với lỗi: `"Kho lưu trữ không hỗ trợ URL đọc trực tiếp."`

### 3.4 Ký Phát Presigned URL & Kiểm Tra Nguồn Gốc (Origin Isolation)
- **REQ-RA-10 (Event-driven):** `WHEN` các điều kiện an toàn đều thỏa mãn, hệ thống `SHALL` gọi `IObjectReadUrlService.CreateAsync` để ký phát Presigned URL với thời hạn sống cấu hình sẵn (`options.Lifetime`).
- **REQ-RA-11 (Unwanted):** `IF` `purpose == "inline"` VÀ tên miền của URL trả về trùng với tên miền của API chính (`options.PublicApiOrigin`) hoặc không dùng giao thức bảo mật `https` (ngoại trừ localhost), `THEN` hệ thống `SHALL` từ chối với lỗi: `"Origin đọc inline không hợp lệ."` (Chống khai thác Same-Origin XSS).

### 3.5 Xác Nhận TOCTOU & Ghi Nhật Ký Kiểm Toán (Audit Trail)
- **REQ-RA-12 (Event-driven):** `WHEN` URL đã được tạo, hệ thống `SHALL` gọi `AssertStillValidAsync` và nạp lại phiên bản hiện tại để đảm bảo tệp tin không bị thay đổi trong quá trình xử lý.
- **REQ-RA-13 (Event-driven):** `WHEN` hợp lệ, hệ thống `SHALL` ghi bản ghi kiểm toán mới vào `db.AuditLogs`:
  - `TenantId = context.TenantId`
  - `UserId = context.Actor.UserId`
  - `Action = "FileReadAccessIssued"`
  - `EntityName = "FileEntry"`
  - `EntityId = file.Id`
  - `Details = $"Purpose={request.Purpose};DocumentVersion={grant.DocumentVersion};ProviderId={provider.Id}"`
- **REQ-RA-14 (Event-driven):** `WHEN` hoàn tất lưu CSDL và kiểm tra quyền lần cuối thành công, hệ thống `SHALL` trả về HTTP `200 OK` với DTO `FileReadAccessResult`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Short-Lived URLs):** Presigned URL chỉ có hiệu lực trong khoảng thời gian ngắn (tối đa 15 phút) nhằm hạn chế rủi ro lộ đường dẫn.
- **NFR-SEC-02 (Origin Separation):** Bản xem inline bắt buộc phục vụ từ domain/subdomain riêng biệt (hoặc CDN bucket origin) tách biệt khỏi API origin để trình duyệt áp dụng chính sách Same-Origin Policy phòng ngừa tấn công chiếm phiên.

---

## 5. Data Contracts

```csharp
public sealed record GetFileReadAccessQuery(
    Guid FileId, 
    string Purpose
) : IRequest<Result<FileReadAccessResult>>;

public sealed record FileReadAccessResult(
    Uri Url, 
    DateTimeOffset ExpiresAt, 
    long DocumentVersion,
    string ContentType, 
    long SizeBytes, 
    bool SupportsRange
);
```

### Error Schema
| Tình huống vi phạm | Thông báo lỗi trả về |
| :--- | :--- |
| Mục đích không đúng định dạng | `"Mục đích đọc không hợp lệ."` |
| Thiếu cấu hình origin | `"Chưa cấu hình origin API công khai."` |
| Sai quyền hoặc không tìm thấy file | `"Không thể đọc tệp tin."` |
| Định dạng không được phép inline/stream | `"Định dạng không hỗ trợ mục đích đọc này."` |
| Kho không phải S3 | `"Kho lưu trữ không hỗ trợ URL đọc trực tiếp."` |
| Trùng domain hoặc không HTTPS | `"Origin đọc inline không hợp lệ."` |
| File bị đổi giữa chừng | `"Tệp tin đã thay đổi."` |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả đầy đủ 3 chế độ cấp quyền đọc (`download`, `inline`, `stream`).
- [x] Thiết lập danh sách trắng MIME types ngăn ngừa rò rỉ mã độc và XSS.
- [x] Đảm bảo ghi nhật ký kiểm toán `FileReadAccessIssued` đầy đủ.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Đặc tả bao phủ toàn bộ cơ chế cấp Presigned URL theo chuẩn an toàn đám mây của OWASP, khớp với mã nguồn hiện hành.
- **Risk:** High.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
