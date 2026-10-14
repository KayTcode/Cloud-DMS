# Feature Specification: Advanced File Search Subsystem (SPEC.md)

- **Feature Slug:** `file-search`
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
Người dùng cần tìm kiếm tài liệu nhanh chóng thông qua từ khóa tiêu đề hoặc nội dung ngữ nghĩa bên trong tệp tin. Phân hệ cung cấp endpoint `GET /api/files/search` hỗ trợ kết hợp tìm kiếm từ khóa SQL/Fulltext và Vector Semantic qua thuật toán Reciprocal Rank Fusion (RRF), đồng thời bảo đảm an toàn dữ liệu nhiều tổ chức (Multi-Tenant) và chống rò rỉ quyền hạn (Anti-IDOR/TOCTOU).

### 1.2 Mục tiêu
- Cung cấp 3 chế độ tìm kiếm: `keyword`, `semantic`, và `hybrid` (mặc định).
- Phân trang dạng con trỏ (`cursor`) có mã hóa và ký số bảo vệ.
- Xác thực quyền truy cập 2 bước trên từng tệp tin trước khi trích xuất đoạn văn bản (Snippet).
- Dự phòng an toàn (Graceful Fallback) khi dịch vụ Vector/Semantic bị trễ hoặc ngưng trệ.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn khi tìm kiếm |
| :--- | :--- | :--- |
| **Mọi tài khoản đã xác thực** | `[Authorize]` | Tìm kiếm trong phạm vi các tệp tin mà tài khoản có quyền đọc (`FileRead`). |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized` (`AUTHENTICATION_REQUIRED`). |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực Yêu cầu
- **REQ-SRCH-01 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files/search` với các tham số (`q`, `mode`, `folderId`, `pageSize`, `cursor`), hệ thống `SHALL` áp dụng bộ lọc Rate Limiting `file-search` và kiểm tra xác thực `[Authorize]`.
- **REQ-SRCH-02 (Unwanted):** `IF` tham số `q` rỗng, dài quá 1000 ký tự, `pageSize < 1` hoặc `pageSize > 100`, hoặc `mode` không nằm trong danh sách `["keyword", "semantic", "hybrid"]`, hoặc `cursor` dài quá 4096 ký tự, `THEN` hệ thống `SHALL` từ chối yêu cầu với HTTP `400 Bad Request` kèm mã lỗi `"INVALID_SEARCH_REQUEST"`.
- **REQ-SRCH-03 (Unwanted):** `IF` request thiếu token xác thực, `THEN` hệ thống `SHALL` trả về HTTP `401 Unauthorized` (`"AUTHENTICATION_REQUIRED"`).

### 3.2 Khởi tạo & Quản lý Phiên Tìm Kiếm (Search Session & Cursor)
- **REQ-SRCH-04 (Event-driven):** `WHEN` request không truyền `cursor` (trang đầu tiên), hệ thống `SHALL` tạo phiên tìm kiếm mới (`SearchSession`), tính toán mã băm yêu cầu `RequestHash = SHA256(Query, Mode, FolderId, PageSize, Model)` và lưu vào bộ nhớ phiên (`ISearchSessionStore`) với thời hạn `SearchPolicy.Lifetime` (10 phút).
- **REQ-SRCH-05 (Event-driven):** `WHEN` request có truyền `cursor`, hệ thống `SHALL` giải mã con trỏ bằng `sessions.Unprotect(cursor)` và kiểm tra:
  - `cursor.UserId == context.Actor.UserId`
  - `cursor.ActorSessionId == context.Actor.SessionId`
  - `cursor.TenantId == context.TenantId`
  - `cursor.RequestHash == hash`
  - `cursor.ExpiresAt > DateTimeOffset.UtcNow`
- **REQ-SRCH-06 (Unwanted):** `IF` con trỏ không hợp lệ, bị sửa đổi, hết hạn hoặc phiên tìm kiếm trong cache không còn tồn tại, `THEN` hệ thống `SHALL` trả về HTTP `400 Bad Request` (`"INVALID_SEARCH_REQUEST"`).

### 3.3 Thu Thập Ứng Viên & Xếp Hạng Hợp Nhất (Candidate Retrieval & RRF)
- **REQ-SRCH-07 (State-driven):** `WHILE` chế độ tìm kiếm yêu cầu từ khóa (`mode == "keyword"` hoặc `mode == "hybrid"`), hệ thống `SHALL` truy vấn `IKeywordSearchService` để lấy danh sách ứng viên phù hợp với từ khóa trong phạm vi ngân sách `CandidateBudget`.
- **REQ-SRCH-08 (State-driven):** `WHILE` chế độ tìm kiếm yêu cầu ngữ nghĩa (`mode == "semantic"` hoặc `mode == "hybrid"`), hệ thống `SHALL` tạo vector embedding cho query qua `IEmbeddingService` (với timeout giới hạn 4 giây) và tìm kiếm trên `IVectorSearchService`.
- **REQ-SRCH-09 (Event-driven):** `WHEN` thực thi ở chế độ `hybrid`, hệ thống `SHALL` kết hợp thứ tự của các ứng viên từ khóa và ứng viên vector bằng thuật toán `ReciprocalRankFusion.Rank` để tạo danh sách hợp nhất.
- **REQ-SRCH-10 (Unwanted):** `IF` dịch vụ Embedding hoặc Vector Search bị lỗi/timeout trong chế độ `hybrid`, `THEN` hệ thống `SHALL` tự động chuyển sang chế độ `keyword` (Graceful Degradation), loại bỏ ứng viên semantic, đặt cờ `Degraded = true` và bổ sung cảnh báo `Warnings = ["SEMANTIC_UNAVAILABLE"]`.
- **REQ-SRCH-11 (Unwanted):** `IF` dịch vụ Semantic bị lỗi trong chế độ `semantic` thuần túy, `THEN` hệ thống `SHALL` trả về HTTP `503 Service Unavailable` (`"SEARCH_UNAVAILABLE"`).

### 3.4 Xác Thực Quyền Đọc 2 Bước & Bảo Vệ TOCTOU
- **REQ-SRCH-12 (Ubiquitous):** Hệ thống `SHALL` chỉ chấp nhận các tệp tin ứng viên thỏa mãn:
  - Thuộc đúng Tenant của caller (`f.TenantId == context.TenantId`).
  - Đang ở trạng thái hoạt động (`f.Lifecycle == FileLifecycle.Active` và `!f.IsDeleted`).
  - Trạng thái quét mã độc sạch (`f.ScanStatus == FileScanStatus.Clean`).
  - Phiên bản tài liệu khớp chính xác (`grant.DocumentVersion == candidate.DocumentVersion`).
- **REQ-SRCH-13 (Event-driven):** `WHEN` chuẩn bị đọc nội dung tóm tắt (Snippet), hệ thống `SHALL` kiểm tra quyền qua `IFileAccessService.CheckManyAsync`.
- **REQ-SRCH-14 (Event-driven):** `WHEN` đã đọc xong dữ liệu tóm tắt của trang hiện tại, hệ thống `SHALL` gọi `IFileAccessService.AssertStillValidAsync` để xác nhận quyền truy cập vẫn còn hiệu lực.
- **REQ-SRCH-15 (Unwanted):** `IF` danh tính hoặc quyền của người dùng bị thay đổi giữa chừng (TOCTOU), `THEN` hệ thống `SHALL` trả về HTTP `409 Conflict` (`"AUTHORIZATION_CHANGED"`).

### 3.5 Đóng Gói Phản Hồi & Phân Trang Tiếp Theo
- **REQ-SRCH-16 (Event-driven):** `WHEN` số lượng ứng viên hợp lệ vượt quá `pageSize`, hệ thống `SHALL` tạo mã con trỏ tiếp theo `NextCursor = sessions.Protect(...)` và trả về kèm kết quả.
- **REQ-SRCH-17 (Event-driven):** `WHEN` thành công, hệ thống `SHALL` trả về HTTP `200 OK` với DTO `SearchFilesResponse` (gồm danh sách `Items`, `NextCursor`, `ModeUsed`, `Degraded`, `Warnings`).

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Information Hiding):** Tuyệt đối không để lộ câu lệnh SQL, mã định danh ứng viên không có quyền, văn bản query hoặc thông tin lỗi nội bộ trong phản hồi (áp dụng tại [FileSearchHttpResult.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FileSearchHttpResult.cs)).
- **NFR-PERF-01 (Timeout Budget):** Tổng thời gian xử lý một request tìm kiếm tối đa là 10 giây; thời gian xử lý Embedding/Vector tối đa là 4 giây.
- **NFR-RES-01 (Candidate Limit):** Ngân sách ứng viên tối đa không vượt quá 1000 bản ghi để chống cạn kiệt bộ nhớ máy chủ.

---

## 5. Data Contracts

```csharp
public sealed record SearchFilesQuery(
    string Query,
    string Mode = "hybrid",
    Guid? FolderId = null,
    int PageSize = 20,
    string? Cursor = null
) : IRequest<SearchFilesResponse>, ISensitiveReadRequest;

public sealed record SearchFileItem(
    Guid FileId,
    string Name,
    string ContentType,
    long SizeBytes,
    long DocumentVersion,
    double Score,
    IReadOnlyList<string> Sources,
    string? Snippet
);

public sealed record SearchFilesResponse(
    IReadOnlyList<SearchFileItem> Items,
    string? NextCursor,
    string ModeUsed,
    bool Degraded,
    IReadOnlyList<string> Warnings
);
```

### Error Schema
| HTTP Status | Mã lỗi phản hồi (`code`) | Mô tả nguyên nhân |
| :---: | :--- | :--- |
| `400` | `INVALID_SEARCH_REQUEST` | Tham số không hợp lệ, query rỗng/quá dài, cursor hỏng hoặc hết hạn. |
| `401` | `AUTHENTICATION_REQUIRED` | Chưa đăng nhập hoặc token không hợp lệ. |
| `409` | `AUTHORIZATION_CHANGED` | Quyền truy cập bị thay đổi trong quá trình thực thi truy vấn (TOCTOU). |
| `499` / `504` | `SEARCH_CANCELLED` | Client hủy request (499) hoặc request vượt quá thời gian chờ (504). |
| `503` | `SEARCH_UNAVAILABLE` | Dịch vụ tìm kiếm ngữ nghĩa gặp sự cố trong chế độ semantic thuần túy hoặc lỗi hệ thống. |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả chi tiết 3 chế độ tìm kiếm và thuật toán RRF.
- [x] Đặc tả quy trình bảo vệ con trỏ phân trang bằng mã hóa (Protected Cursor).
- [x] Đảm bảo quy tắc bảo mật xác thực quyền đọc 2 bước và Fail-Closed tuyệt đối.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Đặc tả đầy đủ toàn bộ luồng tìm kiếm nâng cao theo chuẩn EARS, bảo mật phòng thủ chiều sâu (Defense-in-Depth) khớp với code thực tế.
- **Risk:** High (Core Search Subsystem).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
