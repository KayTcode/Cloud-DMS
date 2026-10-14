# Feature Specification: File Text Extraction Subsystem (SPEC.md)

- **Feature Slug:** `file-get-text`
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
Người dùng hoặc các dịch vụ AI cần đọc nội dung chữ (plain text) đã trích xuất từ các tài liệu số hóa trong hệ thống Cloud-DMS. Endpoint `GET /api/files/{id}/text` phục vụ việc truy xuất tuần tự các khối văn bản (`FileTextChunk`) theo trang/đoạn, bảo đảm an toàn dữ liệu nhiều tổ chức và loại trừ các tài liệu chưa được kiểm duyệt an ninh.

### 1.2 Mục tiêu
- Phân đoạn văn bản hiệu quả với tham số `limit` (từ 1 đến 100) và con trỏ bảo vệ `cursor`.
- Kiểm tra trạng thái sẵn sàng của tiến trình trích xuất văn bản (`ready`, `pending`, `unavailable`).
- Xác thực quyền truy cập hai lớp chống TOCTOU và bảo vệ con trỏ phân trang bằng chữ ký số.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn khi đọc văn bản |
| :--- | :--- | :--- |
| **Người dùng có quyền đọc tài liệu** | `[Authorize(Policy = "file.read")]` | Đọc các khối văn bản trích xuất từ tệp tin thuộc quyền hạn. |
| **Không có quyền hoặc khác Tenant** | Không có quyền | Bị từ chối với HTTP `404 Not Found` (`unavailable`). |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized` (`AUTHENTICATION_REQUIRED`). |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực Yêu Cầu
- **REQ-TXT-01 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files/{id}/text` kèm các tham số (`limit`, `cursor`), hệ thống `SHALL` kiểm tra chính sách `[Authorize(Policy = "file.read")]`, áp dụng Rate Limiting `file-search` và thiết lập header `Cache-Control: no-store`.
- **REQ-TXT-02 (Unwanted):** `IF` tham số `limit < 1` hoặc `limit > 100`, `THEN` hệ thống `SHALL` từ chối yêu cầu với HTTP `400 Bad Request` (`"INVALID_TEXT_CURSOR_OR_LIMIT"`).
- **REQ-TXT-03 (Event-driven):** `WHEN` Use Case `GetFileTextQuery` được thực thi, hệ thống `SHALL` gọi `IFileAccessService.CheckAsync` để kiểm tra quyền `PermissionNames.FileRead`.
- **REQ-TXT-04 (Unwanted):** `IF` quyền đọc không hợp lệ hoặc tệp tin không tồn tại, `THEN` hệ thống `SHALL` trả về kết quả `unavailable` và ánh xạ sang HTTP `404 Not Found`.

### 3.2 Giải Mã & Xác Thực Con Trỏ Phân Trang (Protected Cursor)
- **REQ-TXT-05 (Event-driven):** `WHEN` request có truyền tham số `cursor`, hệ thống `SHALL` giải mã qua `IFileTextCursorProtector.Unprotect(cursor)` và xác thực các điều kiện bất biến:
  - `cursor.ExpiresAt > DateTimeOffset.UtcNow`
  - `cursor.UserId == context.Actor.UserId`
  - `cursor.SessionId == context.Actor.SessionId`
  - `cursor.TenantId == context.TenantId`
  - `cursor.FileId == request.FileId`
  - `cursor.DocumentVersion == grant.DocumentVersion`
  - `cursor.Ordinal >= 0`
- **REQ-TXT-06 (Unwanted):** `IF` bất kỳ điều kiện nào ở REQ-TXT-05 bị vi phạm, `THEN` hệ thống `SHALL` trả về HTTP `400 Bad Request` (`"INVALID_TEXT_CURSOR_OR_LIMIT"`).

### 3.3 Kiểm Tra Thế Hệ Tìm Kiếm (Search Generation) & Quét Mã Độc
- **REQ-TXT-07 (Event-driven):** `WHEN` xác thực bước đầu thành công, hệ thống `SHALL` truy vấn bản ghi thế hệ tìm kiếm `FileSearchGeneration` hiện tại liên kết với tệp tin và phiên bản tài liệu.
- **REQ-TXT-08 (Ubiquitous):** Tệp tin chỉ được xem là hợp lệ khi:
  - `f.Lifecycle == FileLifecycle.Active` và `!f.IsDeleted`.
  - Trạng thái quét mã độc của tệp tin và phiên bản nội dung là `Clean` (`f.ScanStatus == "Clean"` và `v.ScanStatus == FileScanStatus.Clean`).
  - Bản ghi vật lý chưa bị đánh dấu hủy (`v.PhysicalStorageDeletedAt == null`).
  - Trạng thái thế hệ tìm kiếm là `Ready` hoặc `Partial` (`g.Status == FileSearchStatus.Ready || g.Status == FileSearchStatus.Partial`).
- **REQ-TXT-09 (Unwanted):** `IF` thế hệ tìm kiếm chưa tồn tại hoặc đang trong quá trình bóc tách, `THEN` hệ thống `SHALL` xác thực lại quyền qua `AssertStillValidAsync` và trả về trạng thái `pending` (HTTP `202 Accepted`) kèm mã lý do `"TEXT_NOT_READY"`.

### 3.4 Đọc Dữ Liệu Phân Đoạn & Tạo Con Trỏ Kế Tiếp
- **REQ-TXT-10 (Event-driven):** `WHEN` thế hệ tìm kiếm đã sẵn sàng, hệ thống `SHALL` đọc tối đa `limit + 1` bản ghi `FileTextChunk` có `c.TenantId == context.TenantId && c.GenerationId == generation.Id && c.Ordinal >= ordinal` sắp xếp tăng dần theo `c.Ordinal`.
- **REQ-TXT-11 (Event-driven):** `WHEN` số lượng bản ghi vượt quá `limit`, hệ thống `SHALL` lấy `limit` phần tử đầu tiên và sinh con trỏ kế tiếp `NextCursor` qua `protector.Protect(...)` với vị trí mới là `items[^1].Ordinal + 1`.
- **REQ-TXT-12 (Event-driven):** `WHEN` hoàn tất, hệ thống `SHALL` gọi `AssertStillValidAsync` để xác nhận quyền truy cập và kiểm tra phiên bản thế hệ không bị thay đổi, sau đó trả về HTTP `200 OK` kèm danh sách đoạn văn bản (`Items`) và `NextCursor`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Tamper-Proof Cursor):** Con trỏ phân trang phải được ký số HMAC và mã hóa đối xứng, ngăn chặn việc client tự ý sửa đổi số thứ tự `Ordinal` để vượt quyền hoặc dò quét dữ liệu ngoài phạm vi.
- **NFR-PERF-01 (Query Isolation):** Toàn bộ truy vấn đọc `FileTextChunk` phải sử dụng `AsNoTracking()` và lọc trực tiếp trên chỉ mục `(TenantId, GenerationId, Ordinal)` để đạt tốc độ truy xuất dưới 50ms.

---

## 5. Data Contracts

```csharp
public sealed record GetFileTextQuery(
    Guid FileId, 
    int Limit = 20, 
    string? Cursor = null
) : IRequest<FileTextResult>, ISensitiveReadRequest;

public sealed record FileTextItem(
    int Ordinal, 
    string Text, 
    string? Location
);

public sealed record FileTextResult(
    string Status, 
    long DocumentVersion, 
    IReadOnlyList<FileTextItem> Items,
    string? NextCursor = null, 
    bool IsPartial = false, 
    string? Reason = null
);
```

### HTTP Response Mapping
| Trạng thái `Status` | HTTP Status Code | Ghi chú |
| :--- | :---: | :--- |
| `"ready"` | `200 OK` | Đọc thành công danh sách các khối văn bản. |
| `"pending"` | `202 Accepted` | Tiến trình OCR / trích xuất văn bản đang chạy. |
| `"unavailable"` | `404 Not Found` | Không tìm thấy tệp tin hoặc không có quyền. |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả chi tiết cấu trúc phân đoạn văn bản và bảo vệ con trỏ `FileTextCursor`.
- [x] Ràng buộc điều kiện an ninh mã độc và kiểm tra thế hệ tìm kiếm.
- [x] Đặc tả đầy đủ ánh xạ mã HTTP và bảo vệ TOCTOU.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Đặc tả đáp ứng trọn vẹn nghiệp vụ đọc văn bản số hóa, tương thích cao với kiến trúc AI Search của Cloud-DMS.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
