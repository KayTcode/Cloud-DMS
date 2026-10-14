# SDD Phase 0: Context Discovery — File Text Extraction Subsystem

- **Feature Slug:** `file-get-text`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, mô hình dữ liệu và kiểm soát an ninh cho tính năng đọc văn bản trích xuất từ tài liệu (`GetFileText`), hỗ trợ phân trang theo đoạn văn bản (`FileTextChunk`), bảo vệ con trỏ phân trang bằng token mã hóa (`FileTextCursor`), và đồng bộ trạng thái trích xuất theo thế hệ tìm kiếm (`FileSearchGeneration`).
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `GET /api/files/{id}/text` cho phép người dùng đọc nội dung văn bản thuần túy được bóc tách từ tệp tin (thông qua OCR, bộ parser văn bản DOCX, PDF, XLSX, v.v.).
2. Hỗ trợ phân trang đoạn văn bản theo thứ tự xuất hiện (`Ordinal`), nhận tham số `limit` (tối đa 100 đoạn/trang) và chuỗi con trỏ `cursor`.
3. Bảo vệ con trỏ văn bản (`IFileTextCursorProtector`): Mã hóa thông tin vị trí phân trang gắn liền với `UserId`, `SessionId`, `TenantId`, `FileId`, `DocumentVersion`, `GenerationId`, `ModelVersion` với thời hạn tối đa 5 phút.
4. Trả về trạng thái bóc tách văn bản:
   - `ready` (HTTP 200): Văn bản đã trích xuất hoàn tất và sẵn sàng để đọc.
   - `pending` (HTTP 202): Quá trình OCR/bóc tách văn bản đang diễn ra trong background.
   - `unavailable` (HTTP 404): Tệp tin không tồn tại, đã bị xóa hoặc người dùng không có quyền truy cập.
5. Bảo đảm tính toàn vẹn dữ liệu: Chỉ phục vụ các chunk văn bản khi tệp tin có trạng thái quét virus `Clean` và bản ghi thế hệ tìm kiếm có trạng thái `Ready` hoặc `Partial`.

### WHY (Tại sao cần tính năng này)
- **Truy xuất nội dung nhanh cho AI / Chatbot / Search:** Cho phép tích hợp trợ lý ảo tóm tắt tài liệu, trích xuất đoạn văn bản liên quan và hiển thị nội dung trực tiếp cho người khiếm thị hoặc thiết bị không hỗ trợ đọc định dạng phức tạp.
- **Tối ưu truyền tải văn bản:** Thay vì chuyển đổi toàn bộ file hàng chục trang, hệ thống phân đoạn thành các chunk nhỏ (ví dụ 500-1000 từ) có vị trí `Location` (trang, đoạn) giúp cuộn mượt mà (Infinite Scroll).
- **Ngăn chặn rò rỉ dữ liệu chưa duyệt an toàn:** Không bao giờ cho phép trích xuất hoặc đọc văn bản từ các tệp tin chưa quét mã độc hoặc bị nhiễm mã độc.

### Definition of Done (DoD của Pha 0)
- [x] Khảo sát kiến trúc lưu trữ phân đoạn văn bản `FileTextChunk` và bảng quản lý thế hệ `FileSearchGeneration`.
- [x] Định hình cơ chế bảo vệ con trỏ `FileTextCursor` và chống thao túng vị trí đọc.
- [x] Xác định các quy tắc bảo mật chống lộ thông tin văn bản trái phép.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Không thể sao chép văn bản từ file scan/ảnh:** Người dùng tải file PDF dạng ảnh scan và không thể copy đoạn văn cần thiết. | Hệ thống OCR chạy ngầm bóc tách thành các đoạn văn bản có vị trí trang rõ ràng, cung cấp API đọc text từng phần. |
| **Treo ứng dụng khi đọc văn bản của tài liệu hàng nghìn trang:** Trả về toàn bộ nội dung của cuốn sách dày gây tràn bộ nhớ client và server. | Hỗ trợ phân đoạn có kiểm soát với tham số `limit` và con trỏ `cursor`, trả về từng khối nhỏ. |
| **Thao túng token để đọc tài liệu phiên bản cũ hoặc tài liệu của người khác:** Kẻ xấu đổi ID tệp trong token phân trang. | Con trỏ phân trang được mã hóa HMAC, kiểm tra chéo `FileId`, `DocumentVersion` và `UserId` trong mỗi lần gọi tiếp theo. |

---

## 3. Domain Glossary

- **FileTextChunk:** Một đoạn văn bản được bóc tách từ tài liệu, có chỉ số thứ tự tuần tự `Ordinal` và thông tin định vị `Location` (ví dụ: `page: 1, paragraph: 3`).
- **FileSearchGeneration:** Bản ghi đại diện cho một đợt bóc tách và chỉ mục hóa nội dung tài liệu theo phiên bản mô hình AI/Parser (`ModelVersion`).
- **FileTextCursor:** Cấu trúc dữ liệu mã hóa lưu trữ trạng thái vị trí đọc kế tiếp `checked(items[^1].Ordinal + 1)` kèm thời hạn sống.

---

## 4. System Boundaries & Target Components

- **API Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L50-L60) (`GetText`)
- **HTTP Result Formatter:** [FilePreviewHttpResult.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilePreviewHttpResult.cs)
- **Use Case Handler:** [GetFileTextQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetFileText/GetFileTextQuery.cs)
- **Entities:** `FileTextChunk`, `FileSearchGeneration`, `FileContentVersion`.
- **Core Interfaces:** `IFileTextCursorProtector`, `IFileAccessService`, `IApplicationDbContext`.

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân hệ trích xuất văn bản hỗ trợ đắc lực cho các tính năng AI và tìm kiếm toàn văn, bảo mật nghiêm ngặt ở cấp độ từng đoạn văn bản.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
