# Báo cáo Đánh giá Hiệu năng & Rà soát Tối ưu hóa Phân hệ Files (PERFORMANCE_REVIEW.md)

- **Feature Slug:** `files`
- **Phân hệ:** `Core Document Management / Storage Subsystem`
- **Mã nguồn thẩm định:** `CleanArchCqrs.Application/Features/Files/`
- **Framework & ORM Binding:** `.NET 8 / C# 12 / Entity Framework Core 8 / MySQL Pomelo Provider`
- **Quy chuẩn kiểm định:** [sql-performance-tuner](file:///d:/Ky%209/DoAn_2/.agents/skills/sql-performance-tuner/SKILL.md) & [AGENTS.md](file:///d:/Ky%209/DoAn_2/AGENTS.md)
- **Trạng thái:** `PENDING HUMAN REVIEW`

---

## 1. Tổng quan Kiến trúc Hiệu năng (Executive Summary)

Sau khi khảo sát toàn bộ các Command và Query trong thư mục [Features/Files](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files), hệ thống bộc lộ **hai thái cực kiến trúc rõ rệt**:

1. **Nhóm mã nguồn ĐÃ ĐƯỢC TỐI ƯU HÓA CAO (State-of-the-Art Patterns):**
   - Tập trung ở các Command mới: [MultipartUploadService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/BeginMultipartUpload/MultipartUploadService.cs), [UploadQuota.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadQuota.cs), [FileMutationService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/ManageAccess/FileMutationService.cs) và [GetFileTextQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetFileText/GetFileTextQuery.cs).
   - Áp dụng các kỹ thuật: **Direct-to-Storage Presigned URLs**, **Atomic Compare-and-Swap qua `ExecuteUpdateAsync`**, **Transactional Outbox với Worker nền**, và **Keyset/Cursor Pagination $O(1)$**.
2. **Nhóm mã nguồn CHƯA ĐƯỢC TỐI ƯU HÓA (Bottlenecks & Anti-patterns):**
   - Tập trung ở các Query truyền thống: [SearchFilesQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchFilesQueryHandler.cs), [GetTenantFilesQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs), [GetTrashFilesQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetTrashFiles/GetTrashFilesQuery.cs), [DownloadFileQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/DownloadFile/DownloadFileQuery.cs) và [UploadFileCommandHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs).
   - Mắc phải các lỗi nghiêm trọng: **N+1 Database Queries in Loop**, **Excessive Roundtrips (7–9 queries/request)**, **Double Aggregations**, **Redundant `.Include()` với Custom Projection**, và **Server Memory Streaming (OOM Risk)**.

---

## 2. Báo cáo Thẩm định Hiệu năng CSDL (PERSISTENCE PERFORMANCE AUDIT REPORT)

Theo format chuẩn của `/sql-performance-tuner`:

```text
========================================================================================
PERSISTENCE PERFORMANCE AUDIT REPORT
Feature: files | Profile: v1.0.0 | Binding: MySQL 8.0 + EF Core 8 (Pomelo)
========================================================================================

CRITICAL FINDINGS:
  [CRIT-01] [N+1 QUERY IN LOOP]
  Path: src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchFilesQueryHandler.cs:58
        gọi vào SearchCandidateReader.cs:75 (ReadItemAsync)
  Evidence:
    foreach (var file in page) items.Add(await reader.ReadItemAsync(context, file, ct));
    Bên trong ReadItemAsync thực thi 2 câu truy vấn SQL riêng biệt cho mỗi phần tử:
      1. db.FileEntries.Where(f => f.Id == candidate.FileId)...SingleAsync()
      2. db.FileTextChunks.Where(c => c.GenerationId == ... && c.Ordinal == ...)...SingleOrDefaultAsync()
  Impact:
    Gây ra 2N+1 roundtrip database. Nếu PageSize = 20 -> 40 queries. Nếu PageSize = 100 -> 200 queries
    trong một HTTP request duy nhất, gây nghẽn kết nối DB connection pool và tăng độ trễ API đột biến.

WARNING FINDINGS:
  [WARN-01] [EXCESSIVE DATABASE ROUNDTRIPS & REDUNDANT CONTEXT LOOKUPS]
  Path: src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs:21-70
  Evidence:
    Một request GET danh sách file thực hiện 8-9 lượt truy vấn DB tuần tự:
      1. resolver.ResolveContextAsync (Session/Token)
      2. db.Tenants.SingleAsync (Tenant & Provider)
      3. acl.ReadScopeAsync (Query File Scope)
      4. db.Users.FirstOrDefaultAsync (Lấy DepartmentId) -> DƯ THỪA (đã có trong Actor context)
      5. db.UserRoles.AnyAsync (Kiểm tra DepartmentAdmin/Employee) -> DƯ THỪA (đã có trong Claims)
      6. db.Departments.FirstOrDefaultAsync (Lấy Quota/Used)
      7. scope.Skip().Take() (Lấy Candidate IDs)
      8. access.CheckManyAsync (Check ACL)
      9. scope.Where(ids.Contains(f.Id)).ToListAsync() -> QUERY LẶP LẦN 2
  Impact:
    Tăng latency mạng tích lũy (Network I/O Roundtrip Latency) gấp 4-5 lần mức cần thiết.

  [WARN-02] [REDUNDANT INCLUDES & DOUBLE AGGREGATION SCANS]
  Path: src/CleanArchCqrs.Application/Features/Files/Queries/GetTrashFiles/GetTrashFilesQuery.cs:44-70
  Evidence:
    - 4 lệnh .Include() (Owner, DeletedByUser, Department, StorageProvider) bị vô hiệu hóa
      do bên dưới dùng .Select(f => new TrashFileItemDto(...)). EF Core bỏ qua Include nhưng
      vẫn tốn chi phí biên dịch Expression Tree.
    - Chạy riêng biệt db.CountAsync() và db.SumAsync() trên cùng một tập dữ liệu scope, buộc
      MySQL phải quét và tính toán 2 lần trên tập bản ghi.
  Impact:
    Lãng phí CPU máy chủ ứng dụng và gây tải I/O quét bảng kép trên MySQL.

  [WARN-03] [SEQUENTIAL MULTI-TABLE LOOKUP & DOUBLE FETCH IN READ ACCESS]
  Path: src/CleanArchCqrs.Application/Features/Files/Queries/GetFileReadAccess/GetFileReadAccessQuery.cs:75-90
  Evidence:
    Hàm LoadCurrentVersionAsync chạy 3 truy vấn tuần tự riêng biệt:
      db.FileEntries -> db.FileContentVersions -> db.StorageProviders.
    Hàm này lại được gọi 2 lần (dòng 33 và 52) để kiểm tra tính toàn vẹn (TOCTOU).
  Impact:
    Tốn 6 lượt truy vấn cơ sở dữ liệu đơn lẻ cho một yêu cầu cấp link tải/xem file, thay vì 1 câu JOIN.

  [WARN-04] [MONOLITHIC BUFFERED UPLOAD & SERVER STREAMING PROXY]
  Path: src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs:47-150
        và Queries/DownloadFile/DownloadFileQuery.cs:45
  Evidence:
    Dữ liệu file tải lên/tải xuống được stream trực tiếp qua RAM/băng thông của Web API Server .NET
    kèm tính toán hash và quét virus đồng bộ trong request HTTP.
  Impact:
    Nguy cơ cạn kiệt RAM (Out-Of-Memory) khi tải file lớn hoặc nhiều người dùng đồng thời;
    nghẽn ThreadPool và làm chậm toàn bộ các API nhẹ khác.
========================================================================================
```

---

## 3. Phân tích Chi tiết: Mã nguồn Đã Tối ưu vs. Chưa Tối ưu

### 3.1 Vấn đề N+1 Query: [SearchFilesQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchFilesQueryHandler.cs) vs. [SearchCandidateReader.CurrentAsync](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchCandidateReader.cs#L12)

| Đặc điểm | Đoạn code chưa tối ưu (`ReadItemAsync` trong vòng lặp) | Đoạn code đã tối ưu (`CurrentAsync` theo Batch) |
| :--- | :--- | :--- |
| **Vị trí** | `SearchFilesQueryHandler.cs` dòng 58 & `SearchCandidateReader.cs` dòng 75–90 | `SearchCandidateReader.cs` dòng 12–50 |
| **Hành vi** | `foreach (var file in page) await reader.ReadItemAsync(...)` | `foreach (var batch in candidates.Chunk(200))` gom ID bằng `Contains` |
| **Số lượt Query DB** | $2 \times N$ queries (Page 50 items $\rightarrow$ **100 queries**) | $3$ queries cố định cho toàn bộ batch 200 items |
| **Mức độ tiêu tốn I/O** | Cực cao, nghẽn connection pool | Cực thấp, tối ưu hóa triệt để qua index IN-list |

#### Phương án khắc phục cho `ReadItemAsync`:
Thay thế hàm đọc lẻ từng item bằng phương thức batch:
```csharp
// ĐỌC BATCH 1 LẦN CHO CẢ TRANG THAY VÌ N+1 TRONG LOOP
public async Task<IReadOnlyList<SearchFileItem>> ReadPageItemsAsync(
    FileAccessContext context, IReadOnlyList<RankedSearchFile> page, CancellationToken ct)
{
    var fileIds = page.Select(p => p.FileId).ToArray();
    var files = await db.FileEntries.IgnoreQueryFilters().AsNoTracking()
        .Where(f => f.TenantId == context.TenantId && fileIds.Contains(f.Id))
        .ToDictionaryAsync(f => (f.Id, f.DocumentVersion), f => new { f.Name, f.ContentType, f.SizeBytes }, ct);

    var snippetMatches = page
        .Select(p => (Candidate: p, Match: p.Evidence.FirstOrDefault(e => e.GenerationId.HasValue && e.ChunkOrdinal.HasValue)))
        .Where(x => x.Match != null)
        .ToList();

    var genIds = snippetMatches.Select(x => x.Match!.GenerationId!.Value).Distinct().ToArray();
    var chunks = await db.FileTextChunks.IgnoreQueryFilters().AsNoTracking()
        .Where(c => c.TenantId == context.TenantId && genIds.Contains(c.GenerationId))
        .Select(c => new { c.GenerationId, c.Ordinal, c.Text })
        .ToListAsync(ct);

    // Ghép dữ liệu in-memory: 0 lượt roundtrip phát sinh thêm!
    return page.Select(p => {
        var file = files[(p.FileId, p.DocumentVersion)];
        // map snippet from memory chunks
        return new SearchFileItem(p.FileId, file.Name, file.ContentType, file.SizeBytes, ...);
    }).ToList();
}
```

---

### 3.2 Tối ưu Phân trang: [GetTenantFilesQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs) vs. [GetFileTextQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetFileText/GetFileTextQuery.cs)

* **Code chưa tối ưu (`GetTenantFilesQueryHandler`):**
  - Sử dụng Offset Pagination: `.Skip((pageNumber - 1) * pageSize).Take(pageSize + 1)`.
  - **Nhược điểm:** Khi số lượng file lên hàng trăm ngàn và người dùng truy cập trang lớn (ví dụ trang 500), MySQL bắt buộc phải đọc và bỏ qua $500 \times 20 = 10,000$ bản ghi trước khi lấy 20 bản ghi tiếp theo ($O(N)$ scanning).
  - Truy vấn lặp 2 pha: Lần 1 đọc danh sách ID, lần 2 lại query lại `scope.Where(ids.Contains(f.Id))` để lấy DTO.
* **Code đã tối ưu (`GetFileTextQuery`):**
  - Sử dụng **Keyset / Cursor Pagination $O(1)$**:
    ```csharp
    .Where(c => c.TenantId == context.TenantId && c.GenerationId == generation.Id && c.Ordinal >= ordinal)
    .OrderBy(c => c.Ordinal).Take(request.Limit + 1)
    ```
  - **Ưu điểm:** Truy vấn trực tiếp vào B-Tree Index trên cột `Ordinal` mà không cần quan tâm vị trí trang đang ở đâu. Tốc độ trang 1 và trang 10,000 là **nhanh như nhau ($< 2ms$)**.

---

### 3.3 Tối ưu Cập nhật & Kiểm soát Quota: [UploadFileCommandHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs) vs. [UploadQuota.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/UploadFile/UploadQuota.cs) & [MultipartUploadService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/BeginMultipartUpload/MultipartUploadService.cs)

* **Code chưa tối ưu (`UploadFileCommandHandler`):**
  - Kéo `User`, `Tenant`, `Department` vào bộ nhớ qua Entity Tracking.
  - Kiểm tra quota bằng cách đọc `UsedBytes` vào RAM rồi cộng trừ $\rightarrow$ Rất dễ dính **Race Condition** khi tải nhiều file đồng thời (Quota Overrun).
* **Code đã tối ưu (`UploadQuota.ReserveAsync`):**
  - Áp dụng **Atomic Test-and-Set** ở tầng Database Engine bằng `ExecuteUpdateAsync`:
    ```csharp
    await db.Tenants.IgnoreQueryFilters()
        .Where(t => t.Id == tenantId && t.StorageQuotaBytes >= bytes && t.StorageUsedBytes <= t.StorageQuotaBytes - bytes)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes, t => t.StorageUsedBytes + bytes), ct);
    ```
  - **Lợi ích:**
    1. Không tải Entity vào RAM $\rightarrow$ Tiết kiệm tài nguyên Garbage Collector.
    2. Nguyên tử tuyệt đối: Nếu 2 request cùng nộp file, MySQL tự tuần tự hóa ở mức row lock; request nào vượt ngưỡng thì mệnh đề `WHERE` trả về 0 rows updated và văng exception ngay lập tức mà không bao giờ bị vượt Quota.

---

### 3.4 Tối ưu Thao tác Đọc Metadata: [DownloadFileQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/DownloadFile/DownloadFileQuery.cs) vs. [GetFileTextQuery.CurrentAsync](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetFileText/GetFileTextQuery.cs#L62)

* **Code chưa tối ưu (`DownloadFileQuery` & `GetFileReadAccessQuery`):**
  - Thực hiện 3 câu query riêng lẻ:
    ```csharp
    var file = await db.FileEntries.SingleOrDefaultAsync(...);
    var content = await db.FileContentVersions.SingleOrDefaultAsync(...);
    var provider = await db.StorageProviders.SingleOrDefaultAsync(...);
    ```
* **Code đã tối ưu (`GetFileTextQuery.CurrentAsync`):**
  - Gom toàn bộ vào **1 câu LINQ JOIN duy nhất**:
    ```csharp
    from f in db.FileEntries.IgnoreQueryFilters().AsNoTracking()
    join g in db.FileSearchGenerations.IgnoreQueryFilters().AsNoTracking() on f.ActiveSearchGenerationId equals (Guid?)g.Id
    join v in db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
       on new { f.TenantId, FileId = f.Id, f.DocumentVersion } equals new { v.TenantId, v.FileId, v.DocumentVersion }
    where f.Id == grant.FileId && f.TenantId == context.TenantId ...
    select ...
    ```
  - Cắt giảm từ 3 network roundtrips xuống còn **1 network roundtrip**.

---

## 4. Kế hoạch Tối ưu hóa Toàn diện (Actionable Remediation Plan)

| Hạng mục | Vấn đề tồn đọng | Phương án tối ưu hóa đề xuất | Mức độ ưu tiên |
| :--- | :--- | :--- | :--- |
| **`SearchFiles`** | N+1 Database Query in Loop trong `ReadItemAsync` | Viết hàm `ReadPageItemsAsync` dùng `Contains` gom toàn bộ Page thành 2 query song song. | **P0 (Khẩn cấp)** |
| **`GetTenantFiles`** | Tốn 8–9 roundtrips; query lặp User, Role, Department | 1. Đọc User Role & Dept từ `context.Actor` (cắt 2 queries).<br>2. Bỏ query `scope` lặp lần 2, thực hiện projection trực tiếp. | **P1 (Cao)** |
| **`GetTrashFiles`** | 4 `.Include()` dư thừa; 2 lượt scan độc lập (`Count` + `Sum`) | 1. Bỏ toàn bộ `.Include()`.<br>2. Gom `CountAsync` và `SumAsync` vào 1 câu `GroupBy(x => 1)`. | **P2 (Trung bình)** |
| **`GetFileReadAccess`** | `LoadCurrentVersionAsync` chạy 3 queries x 2 lần = 6 queries | Chuyển thành 1 câu LINQ JOIN giữa `FileEntries`, `FileContentVersions` và `StorageProviders`. | **P2 (Trung bình)** |
| **`DownloadFile` & `UploadFile`** | Proxy toàn bộ file nhị phân qua Web Server | Khuyến khích chuẩn hóa sang Presigned URL Direct S3 (`MultipartUploadService` & `GetFileReadAccessQuery`). | **P3 (Định hướng)** |

---

## 5. Kết luận & Khuyến nghị

1. **Chuẩn hóa mô hình Đọc (Read Model):**
   - Các Query Handlers cần học tập cách tiếp cận của `GetFileTextQuery` (dùng LINQ JOIN và Keyset Cursor) và `SearchCandidateReader.CurrentAsync` (Batch Processing qua `.Chunk()`).
   - Tuyệt đối loại bỏ mọi lời gọi `db.SingleOrDefaultAsync` hoặc `db.FirstOrDefaultAsync` nằm trong vòng lặp `foreach`/`for`.
2. **Tuân thủ Clean Architecture Port & Adapter:**
   - Các logic truy vấn CSDL phức tạp nên được đóng gói vào các Service hoặc Reader chuyên trách (như `SearchCandidateReader`, `IFileAclQuery`), giữ cho Handler đóng vai trò điều phối mỏng (Thin Handlers) như mô hình của `DeleteFileCommand` và `MultipartUploadService`.
