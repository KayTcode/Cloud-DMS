# SDD Phase 0: Context Discovery — Advanced File Search Subsystem

- **Feature Slug:** `file-search`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới kiến trúc và bảo mật cho tính năng tìm kiếm tệp tin nâng cao (`SearchFilesQuery`), hỗ trợ 3 chế độ tìm kiếm (`keyword`, `semantic`, `hybrid`), kết hợp xếp hạng hợp nhất `ReciprocalRankFusion` (RRF), phân trang con trỏ mã hóa an toàn (`Protected Cursor`), kiểm soát quyền đọc phân tầng (`IFileAccessService`) và cơ chế tự phục hồi suy giảm dịch vụ (Graceful Degradation).
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `GET /api/files/search` tiếp nhận truy vấn từ khóa `q`, chế độ `mode` (`keyword`, `semantic`, `hybrid`), giới hạn thư mục tùy chọn `folderId`, kích thước trang `pageSize` (1–100) và con trỏ phân trang `cursor`.
2. Hỗ trợ đa chế độ tìm kiếm:
   - **`keyword`:** Tìm kiếm toàn văn bản (Full-text) và tên tệp tin qua dịch vụ từ khóa SQL / Search Engine.
   - **`semantic`:** Tìm kiếm ngữ nghĩa qua mô hình vector embedding (chuyển đổi câu hỏi thành vector biểu diễn và tìm các đoạn nội dung tương tự trong Vector DB).
   - **`hybrid`:** Kết hợp cả từ khóa và ngữ nghĩa bằng thuật toán xếp hạng `Reciprocal Rank Fusion` (RRF) để tối ưu độ chính xác và liên quan.
3. Cơ chế phân trang theo phiên (`SearchSession`):
   - Lưu trữ danh sách ứng viên (candidate pool) có thời hạn (TTL) và cấp con trỏ `NextCursor` được mã hóa/bảo vệ HMAC (`sessions.Protect`) gắn liền với `UserId`, `SessionId`, `TenantId` và hàm băm request.
4. Bảo mật Fail-Closed & Chống suy diễn IDOR/BOLA:
   - Chỉ lọc các tệp tin thuộc cùng Tenant của caller, có trạng thái hoạt động (`Active`), bản ghi quét mã độc sạch (`Clean`).
   - Kiểm tra quyền truy cập 2 lần: Trước khi đọc nội dung tóm tắt (`CheckManyAsync`) và sau khi chuẩn bị kết quả (`AssertStillValidAsync`) để ngăn chặn việc rò rỉ dữ liệu khi quyền thay đổi giữa chừng (Time-of-Check to Time-of-Use - TOCTOU).
5. Dự phòng suy giảm dịch vụ an toàn (Graceful Degradation):
   - Khi dịch vụ Vector/Embedding gặp sự cố hoặc timeout (quá 4 giây), chế độ `hybrid` tự động suy thoái xuống chế độ `keyword` với cảnh báo `SEMANTIC_UNAVAILABLE` thay vì báo lỗi toàn bộ hệ thống.

### WHY (Tại sao cần tính năng này)
- **Tìm kiếm thông minh trong doanh nghiệp:** Người dùng không chỉ nhớ chính xác tên file mà thường chỉ nhớ nội dung, chủ đề hoặc khái niệm bên trong tài liệu. Tìm kiếm ngữ nghĩa kết hợp từ khóa giúp tra cứu tài liệu nhanh chóng và chính xác.
- **Bảo mật nội dung tài liệu mật:** Kết quả tìm kiếm và đoạn trích (snippet) tuyệt đối không được hiển thị cho người không có quyền đọc tài liệu.
- **Chống quá tải và cạn kiệt tài nguyên (Resource Exhaustion / DoS):** Giới hạn ngân sách ứng viên tìm kiếm (`CandidateBudget = 1000`), thời gian xử lý timeout nghiêm ngặt (10s tổng, 4s cho semantic) và áp dụng Rate Limiting `file-search`.

### Definition of Done (DoD của Pha 0)
- [x] Khảo sát chi tiết 3 chế độ tìm kiếm `keyword`, `semantic`, `hybrid` và thuật toán kết hợp RRF.
- [x] Xác định kiến trúc lưu trữ phiên và bảo vệ con trỏ phân trang `SearchSession` / `Cursor`.
- [x] Đặc tả ranh giới bảo mật đa người thuê, chống TOCTOU và cơ chế kiểm tra quyền truy cập 2 lớp.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Không tìm được tài liệu khi quên tên:** Tính năng tìm kiếm cũ chỉ tìm `LIKE %name%` theo tên tệp, không thể tìm theo nội dung bên trong file. | Cung cấp tìm kiếm toàn văn và tìm kiếm ngữ nghĩa theo nội dung văn bản bóc tách (`FileTextChunk`), xếp hạng điểm số liên quan bằng RRF. |
| **Rò rỉ trích đoạn tài liệu mật (Data Leakage):** Người dùng tìm kiếm thấy đoạn trích nội dung (Snippet) của tài liệu thuộc phòng ban khác mà mình không có quyền xem. | Sử dụng `IFileAccessService.CheckManyAsync` và `AssertStillValidAsync` xác thực quyền đọc trên từng tệp tin ứng viên trước khi trích xuất snippet và trả về cho client. |
| **Treo hệ thống khi dịch vụ Vector/AI gặp sự cố:** Dịch vụ Embedding AI bị chậm hoặc chết làm tê liệt tính năng tìm kiếm của cả công ty. | Áp dụng timeout độc lập (4s cho semantic) và tự động suy giảm dịch vụ (Graceful Fallback) về tìm kiếm từ khóa thuần túy, kèm cảnh báo `Degraded = true`. |
| **Thao túng phân trang để đọc trộm dữ liệu:** Kẻ xấu can thiệp vào tham số offset/page để đoán thứ tự file. | Sử dụng con trỏ phân trang mã hóa (`Protected Cursor`) liên kết chặt với Session của người gọi và chữ ký số HMAC. |

---

## 3. Domain Glossary

- **Reciprocal Rank Fusion (RRF):** Thuật toán kết hợp và chuẩn hóa thứ hạng từ nhiều nguồn tìm kiếm độc lập (từ khóa và vector) theo công thức $RRF\_Score(d) = \sum \frac{1}{k + r(d)}$, giúp dung hòa các thang điểm khác nhau.
- **SearchSession:** Bản ghi lưu trữ tập ứng viên tìm kiếm trong bộ nhớ đệm (Cache/Redis), giới hạn bởi TTL ngắn (ví dụ: 10 phút), gắn liền với chữ ký request `RequestHash`.
- **Protected Cursor:** Chuỗi token phân trang đã được mã hóa và ký số chứa thông tin vị trí `position`, `sessionId`, `tenantId`, `userId`, ngăn chặn việc giả mạo tham số phân trang.
- **Degraded Search:** Trạng thái hệ thống chuyển đổi từ tìm kiếm lai (Hybrid) sang tìm kiếm từ khóa truyền thống khi phân hệ Semantic/Embedding không khả dụng.

---

## 4. System Boundaries & Target Components

- **API Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs) (`Search` action tại `/api/files/search`)
- **Result Filter / Error Boundary:** [FileSearchHttpResult.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FileSearchHttpResult.cs)
- **Use Case Handler:** [SearchFilesQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchFilesQueryHandler.cs)
- **Query & DTOs:** [SearchFilesQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchFilesQuery.cs)
- ** thuật toán RRF:** [ReciprocalRankFusion.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/ReciprocalRankFusion.cs)
- **Candidate Reader:** [SearchCandidateReader.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchCandidateReader.cs)
- **Core Interfaces:** `IKeywordSearchService`, `IEmbeddingService`, `IVectorSearchService`, `ISearchSessionStore`, `IFileAccessService`.

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Tài liệu bối cảnh phản ánh chính xác cấu trúc và logic mã nguồn hiện có của tính năng `SearchFilesQueryHandler`, tuân thủ các nguyên tắc an ninh cấp cao và Clean Architecture.
- **Risk:** High (Truy vấn dữ liệu đa phương thức, kiểm soát quyền truy cập nhạy cảm).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
