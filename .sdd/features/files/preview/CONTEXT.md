# SDD Phase 0: Context Discovery — File Preview Subsystem

- **Feature Slug:** `file-preview`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới kiến trúc và giao thức phản hồi trạng thái cho hệ thống tạo và xem trước tệp tin (`RequestFilePreview` và `GetFilePreview`), tích hợp nhà cung cấp tạo bản xem trước (`IPreviewProvider`), bảo vệ quyền truy cập (`file.read`) và ánh xạ mã trạng thái HTTP chuẩn mực.
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API yêu cầu tạo/kích hoạt xử lý xem trước tệp tin bất đồng bộ: `POST /api/files/{id}/preview`.
2. Cung cấp API thăm dò (polling) hoặc lấy thông tin bản xem trước: `GET /api/files/{id}/preview`.
3. Quản lý trạng thái vòng đời của bản xem trước (`PreviewResult`):
   - **`ready` (HTTP 200):** Bản xem trước đã sẵn sàng, trả về URL an toàn hoặc dữ liệu hiển thị.
   - **`pending` (HTTP 202):** Đang xử lý tạo bản xem trước trong background worker.
   - **`unavailable` (HTTP 404):** Tệp tin không tồn tại, đã bị xóa hoặc người dùng không có quyền đọc.
   - **`unsupported` (HTTP 422):** Định dạng tài liệu không được hỗ trợ để tạo bản xem trước (ví dụ: file nhị phân nén, định dạng độc quyền lạ).
   - **`failed` (HTTP 503):** Tiến trình render xem trước gặp sự cố hệ thống.
4. Bảo vệ dữ liệu với tiêu đề `Cache-Control: no-store` và giới hạn tần suất `EnableRateLimiting("file-search")`.

### WHY (Tại sao cần tính năng này)
- **Trải nghiệm người dùng mượt mà:** Cho phép người dùng duyệt nhanh nội dung tài liệu (PDF, hình ảnh, văn bản Office cơ bản) trực tiếp trên trình duyệt mà không cần tải toàn bộ tệp tin gốc về máy tính.
- **Tối ưu băng thông & Thiết bị di động:** Bản xem trước thường được nén hoặc chuyển đổi thành hình ảnh thu nhỏ/PDF tối ưu, giảm đáng kể dung lượng mạng so với file gốc hàng chục MB.
- **Ngăn chặn tấn công tài nguyên:** Quá trình chuyển đổi định dạng phức tạp (như LibreOffice, PDF rendering) tiêu tốn nhiều CPU/RAM nên được điều phối qua tiến trình background với mã HTTP `202 Accepted`.

### Definition of Done (DoD của Pha 0)
- [x] Khảo sát hai luồng yêu cầu kích hoạt (`POST`) và thăm dò kết quả (`GET`).
- [x] Định hình bảng ánh xạ mã trạng thái HTTP theo kết quả của `PreviewResult`.
- [x] Xác định nguyên tắc bảo vệ quyền đọc chống IDOR trước khi cung cấp URL preview.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Phải tải toàn bộ tệp lớn về máy mới xem được:** Người dùng chỉ muốn lướt qua xem nội dung file có đúng tài liệu cần tìm không nhưng phải tốn thời gian tải cả tệp tin lớn. | Cung cấp giao diện xem trước nhanh (Preview), tải trước trang đầu hoặc bản thu gọn trực tiếp trên Web UI. |
| **Treo trình duyệt khi mở định dạng không hỗ trợ:** Client gửi yêu cầu xem trước nhưng server đứng im hoặc trả lỗi chung chung 500. | Trả về mã lỗi rõ ràng: `422 Unprocessable Entity` khi định dạng không hỗ trợ, `202 Accepted` khi đang xử lý để FE hiển thị biểu tượng đang tải (Loading Spinner). |
| **Xem trộm nội dung qua đường dẫn preview:** Kẻ xấu đoán URL preview để xem tài liệu của người khác. | Bản xem trước tuân thủ phân quyền `file.read` và xác thực chữ ký truy cập tạm thời có thời hạn ngắn. |

---

## 3. Domain Glossary

- **PreviewResult:** Đối tượng chuyển giao dữ liệu chứa trạng thái xem trước (`Status`), phiên bản tài liệu (`DocumentVersion`), và mã lỗi/thông điệp tương ứng.
- **Preview Provider:** Phân hệ dịch vụ (`IPreviewProvider`) chịu trách nhiệm giao tiếp với bộ chuyển đổi tài liệu (Document Conversion Worker / LibreOffice / PDFium / ImageMagick).
- **Asynchronous Preview Generation:** Quy trình tạo bản xem trước bất đồng bộ: Request đầu tiên trả về `202 Accepted`, worker chạy ngầm, request tiếp theo sẽ nhận `200 OK`.

---

## 4. System Boundaries & Target Components

- **API Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L28-L46) (`RequestPreview` và `GetPreview`)
- **HTTP Result Formatter:** [FilePreviewHttpResult.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilePreviewHttpResult.cs)
- **Command & Query:** 
  - [RequestFilePreviewCommand.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/RequestFilePreview/RequestFilePreviewCommand.cs)
  - [GetFilePreviewQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetFilePreview/GetFilePreviewQuery.cs)
- **Core Interfaces:** `IPreviewProvider`, `IFileAccessContextResolver`, `IFileAccessService`.

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân hệ Preview đáp ứng chuẩn RESTful với các mã HTTP phân định rõ trạng thái xử lý bất đồng bộ và kiểm soát quyền đọc chặt chẽ.
- **Risk:** Medium (Chuyển đổi dữ liệu và phân quyền đọc).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
