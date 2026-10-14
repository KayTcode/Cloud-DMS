# SDD Phase 0: Context Discovery — Direct Read Access & Presigned URL Subsystem

- **Feature Slug:** `file-read-access`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, mô hình an ninh và cơ chế cấp quyền đọc trực tiếp qua liên kết có chữ ký số (Presigned Read URL) từ kho lưu trữ đám mây (S3 Storage) cho các mục đích tải về (`download`), hiển thị nội tuyến (`inline`), hoặc truyền phát đa phương tiện (`stream`), kèm kiểm soát an toàn nguồn gốc (`PublicApiOrigin`) và ghi nhật ký kiểm toán (`AuditLog`).
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `GET /api/files/{id}/read-access` cho phép tạo liên kết đọc trực tiếp từ Cloud Storage (như Amazon S3, MinIO) với thời hạn truy cập ngắn (`Lifetime`).
2. Hỗ trợ 3 mục đích truy cập (`purpose`):
   - **`download`:** Tải tệp tin về máy trạm với header Content-Disposition dạng attachment.
   - **`inline`:** Hiển thị trực tiếp trên trình duyệt (chỉ cho phép các định dạng an toàn: PDF, ảnh PNG/JPEG/GIF/WEBP, văn bản thuần TXT).
   - **`stream`:** Truyền phát âm thanh/video với hỗ trợ phân đoạn byte range (MP3, MP4, OGG, WAV, WEBM).
3. Kiểm tra tính tương thích của nhà cung cấp lưu trữ: Chỉ hỗ trợ cho Storage Provider có khả năng sinh Presigned URL (`StorageProviderType.S3`).
4. Ghi nhận dấu vết an ninh: Mọi lần cấp Presigned URL thành công đều được ghi lại vào bảng `AuditLogs` với hành động `FileReadAccessIssued`.

### WHY (Tại sao cần tính năng này)
- **Giảm tải băng thông và tải trọng CPU máy chủ API:** Với các tệp tin lớn (video, tài liệu hàng trăm MB), việc tải trực tiếp từ máy chủ Cloud Storage giúp Web API Server không bị nghẽn mạng (Network Bottleneck).
- **Hỗ trợ tua video/audio (Range Requests):** Trình phát đa phương tiện của trình duyệt yêu cầu HTTP Range Request (206 Partial Content) để tua nhanh, điều mà Object Storage đám mây hỗ trợ tốt hơn truyền qua Web API Proxy.
- **An toàn tuyệt đối:** Liên kết Presigned có thời hạn sống ngắn (vài phút), không tiết lộ AccessKey/SecretKey của Storage Provider, và được bảo vệ bởi quyền `file.read`.

### Definition of Done (DoD của Pha 0)
- [x] Phân loại rõ ràng 3 mục đích truy cập (`download`, `inline`, `stream`) và danh sách trắng MIME types cho phép.
- [x] Xác định quy trình ký URL qua `IObjectReadUrlService` và kiểm tra nguồn gốc `PublicApiOrigin`.
- [x] Quy định nhật ký kiểm toán bắt buộc cho hành động cấp quyền truy cập đọc.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Không tua được video/nhạc khi nghe trực tuyến:** Tải qua stream trung gian của API thường không hỗ trợ header `Accept-Ranges` đầy đủ. | Cấp Presigned URL từ S3 hỗ trợ hoàn chỉnh Byte-Range Requests cho mục đích `stream`. |
| **Máy chủ Web sập do quá tải băng thông:** Hàng trăm nhân viên đồng thời tải tài liệu lớn làm nghẽn toàn bộ băng thông của Web API. | Đẩy lưu lượng truyền tải nhị phân trực tiếp về mạng lưới Object Storage của Cloud Provider. |
| **Nguy cơ tấn công XSS qua file HTML/SVG inline:** Kẻ xấu tải file HTML hoặc SVG chứa mã độc script và mở inline trên domain của API. | Whitelist nghiêm ngặt MIME types được phép mở `inline` (chỉ PDF, ảnh chuẩn, text) và cô lập domain qua `PublicApiOrigin`. |

---

## 3. Domain Glossary

- **Presigned Read URL:** Địa chỉ URL có gắn kèm chữ ký mật mã HMAC và thời gian hết hạn do Cloud Storage cấp phát, cho phép client truy cập đối tượng mà không cần thông tin đăng nhập trực tiếp.
- **Safe Content Type:** Chuẩn hóa MIME Type, loại bỏ các tham số bổ sung và xác thực định dạng an toàn trước khi cấp quyền hiển thị trực tiếp.
- **FileReadAccessIssued Audit:** Bản ghi nhật ký hệ thống ghi nhận người dùng, thời điểm, phiên bản tài liệu và nhà cung cấp lưu trữ được trích xuất.

---

## 4. System Boundaries & Target Components

- **API Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L248-L256) (`GetReadAccess`)
- **Use Case Handler:** [GetFileReadAccessQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetFileReadAccess/GetFileReadAccessQuery.cs)
- **Entities & DTOs:** `AuditLog`, `FileReadAccessResult`, `ReadAccessOptions`.
- **Core Interfaces:** `IObjectReadUrlService`, `IFileAccessService`, `IApplicationDbContext`.

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Thiết kế cơ chế Direct Read Access giảm tải vượt trội cho hạ tầng máy chủ, tuân thủ nghiêm ngặt các rào cản phòng chống XSS và BOLA.
- **Risk:** High (Cấp quyền đọc trực tiếp hạ tầng lưu trữ).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
