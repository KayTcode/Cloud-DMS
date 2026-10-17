---
name: efcore-performance-reviewer
description: Rà soát và đề xuất tối ưu hóa hiệu năng EF Core + CQRS theo các pattern chuẩn của dự án (Batch Query, Keyset Pagination, Atomic CAS, Presigned URL, Thin Handler). Áp dụng cho .NET 8 / EF Core 8 / MySQL Clean Architecture.
user-invocable: true
---

# EF Core Performance Reviewer (`/efcore-performance-reviewer`)

**Output language:** Output mirrors the language of the invoking prompt. Vietnamese prompt → Vietnamese output. Canonical tokens (`CRITICAL`, `WARNING`, `PASSED`, `PENDING HUMAN REVIEW`, `CONFIGURATION GAP`, `BLOCKED`), rule codes (`N+1`, `ROUNDTRIP`, `CAS`, `PROJ`, `PAGE`, `IO`), file paths và CLI commands là language-invariant.

Skill này đúc kết từ phân tích thực tế trong [phân hệ Files của Cloud-DMS](file:///d:/Ky%209/DoAn_2/.sdd/features/files/PERFORMANCE_REVIEW.md) (.NET 8 / EF Core 8 / MySQL / Pomelo Provider / CQRS + Clean Architecture).

---

## Tham số

- `--file=<path>`: Handler, Service hoặc Reader cần rà soát (Command Handler, Query Handler, Application Service).
- `--feature=<slug>`: Rà soát toàn bộ feature (duyệt tất cả file trong thư mục tương ứng).
- `--mode=audit|fix|compare`:
  - `audit` — Chỉ phân tích và liệt kê vấn đề.
  - `fix` — Đề xuất đoạn code tối ưu (read-only proposal, không ghi file trừ khi trong `/add-execute` đã approved).
  - `compare` — So sánh file đang xét với một file "đối chiếu" chuẩn tốt trong cùng codebase.
- `--against=<path>`: File mẫu tối ưu để so sánh (dùng với `--mode=compare`).

---

## Architecture Profile Gate

1. Đọc Architecture Profile / AGENTS.md để xác nhận ORM binding (EF Core 8 / Pomelo / MySQL).
2. Xác nhận tech stack: `.NET 8`, `C# 12`, `MediatR`, `IApplicationDbContext`, `Clean Architecture`.
3. Nếu binding chưa có evidence, chỉ audit layer boundary và báo `CONFIGURATION GAP`.
4. Chỉ dùng syntax EF Core 8 (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`, `AsNoTracking`, LINQ JOIN) khi binding đã `APPROVED`.

---

## Bảng Pattern Chuẩn (Knowledge Base)

Skill này sử dụng 6 pattern tối ưu đã được xác nhận từ thực tế codebase:

### Pattern 1 — Chống N+1: Batch Query thay vì Loop
- **Anti-pattern (BAD):**
  ```csharp
  // Gây N+1: mỗi vòng lặp = 1–2 roundtrip database
  foreach (var item in page)
      items.Add(await reader.ReadItemAsync(context, item, ct));
  ```
- **Pattern chuẩn (GOOD) — dựa theo `SearchCandidateReader.CurrentAsync`:**
  ```csharp
  // 1 query duy nhất cho toàn bộ batch tối đa 200 items
  foreach (var batch in candidates.Chunk(200))
  {
      var ids = batch.Select(c => c.FileId).Distinct().ToArray();
      var files = await scope.Where(f => ids.Contains(f.Id))
          .Select(f => new { f.Id, f.DocumentVersion }).ToArrayAsync(ct);
      // ghép kết quả in-memory, 0 roundtrip phát sinh thêm
  }
  ```
- **Rule Code:** `[N+1]` — Severity: `CRITICAL`

---

### Pattern 2 — Cắt giảm Roundtrip: Trích xuất thông tin từ Context thay vì Query lại DB
- **Anti-pattern (BAD):**
  ```csharp
  // DƯ THỪA: DepartmentId và Roles đã có trong context.Actor / Claims
  var currentUser = await db.Users.FirstOrDefaultAsync(u => u.Id == context.Actor.UserId, ct);
  var isDeptScoped = await db.UserRoles.AnyAsync(ur => ur.UserId == context.Actor.UserId
      && (ur.Role.Name == "DepartmentAdmin" || ur.Role.Name == "Employee"), ct);
  ```
- **Pattern chuẩn (GOOD):**
  ```csharp
  // Lấy trực tiếp từ Actor đã resolve bởi IFileAccessContextResolver
  var actorDeptId = context.Actor.DepartmentId;
  var isDeptScoped = context.Actor.Roles.Any(r => r is "DepartmentAdmin" or "Employee");
  ```
- **Rule Code:** `[ROUNDTRIP]` — Severity: `WARNING`

---

### Pattern 3 — Cập nhật Nguyên tử: `ExecuteUpdateAsync` (Atomic CAS) thay vì Load & SaveChanges
- **Anti-pattern (BAD):**
  ```csharp
  // Race condition: đọc rồi mới cộng → dễ vượt Quota khi 2 request đồng thời
  var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, ct);
  tenant.StorageUsedBytes += bytes;
  await db.SaveChangesAsync(ct);
  ```
- **Pattern chuẩn (GOOD) — dựa theo `UploadQuota.ReserveAsync`:**
  ```csharp
  // Atomic Test-and-Set trực tiếp tại DB Engine, không cấp phát RAM cho Entity
  var updated = await db.Tenants.IgnoreQueryFilters()
      .Where(t => t.Id == tenantId
          && t.StorageQuotaBytes >= bytes
          && t.StorageUsedBytes <= t.StorageQuotaBytes - bytes)
      .ExecuteUpdateAsync(s => s.SetProperty(
          t => t.StorageUsedBytes, t => t.StorageUsedBytes + bytes), ct);
  if (updated != 1) throw new UploadQuotaExceededException("Quota exceeded.");
  ```
- **Rule Code:** `[CAS]` — Severity: `CRITICAL` (khi gặp trong vùng concurrent mutation)

---

### Pattern 4 — Phân trang Hiệu quả: Keyset Cursor (`O(1)`) thay vì Offset (`O(N)`)
- **Anti-pattern (BAD):**
  ```csharp
  // Offset Pagination: trang 500 → MySQL phải bỏ qua 500 × 20 = 10.000 bản ghi
  .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize + 1)
  ```
- **Pattern chuẩn (GOOD) — dựa theo `GetFileTextQuery`:**
  ```csharp
  // Keyset Cursor: truy vấn thẳng vào B-Tree Index, tốc độ O(1) bất kể vị trí trang
  .Where(c => c.Ordinal >= cursor.Ordinal)
  .OrderBy(c => c.Ordinal)
  .Take(request.Limit + 1)
  ```
- **Rule Code:** `[PAGE]` — Severity: `WARNING`

---

### Pattern 5 — Tối ưu Projection: LINQ JOIN thay vì Sequential Multi-Table Lookup
- **Anti-pattern (BAD):**
  ```csharp
  // 3 queries tuần tự riêng lẻ = 3 network roundtrips
  var file     = await db.FileEntries.SingleOrDefaultAsync(..., ct);
  var content  = await db.FileContentVersions.SingleOrDefaultAsync(..., ct);
  var provider = await db.StorageProviders.SingleOrDefaultAsync(..., ct);
  ```
- **Pattern chuẩn (GOOD) — dựa theo `GetFileTextQuery.CurrentAsync`:**
  ```csharp
  // 1 câu LINQ JOIN = 1 network roundtrip, 1 execution plan
  var result = await (
      from f in db.FileEntries.IgnoreQueryFilters().AsNoTracking()
      join v in db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
          on new { f.TenantId, FileId = f.Id, f.DocumentVersion }
          equals new { v.TenantId, v.FileId, v.DocumentVersion }
      join p in db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
          on v.ProviderId equals p.Id
      where f.Id == fileId && f.TenantId == tenantId && !f.IsDeleted && p.IsActive
      select new { File = f, Content = v, Provider = p }
  ).SingleOrDefaultAsync(ct);
  ```
- **Rule Code:** `[PROJ]` — Severity: `WARNING`

---

### Pattern 6 — Tối ưu I/O: Direct-to-Storage (Presigned URL) thay vì Server-Side Streaming
- **Anti-pattern (BAD):**
  ```csharp
  // Toàn bộ file đi qua RAM Web Server: nguy cơ OOM khi file lớn hoặc đồng thời nhiều user
  stream = await storageService.DownloadFileAsync(content.ObjectKey, ct);
  return Result.Success(new DownloadFileResult(stream, file.Name, content.ContentType));
  ```
- **Pattern chuẩn (GOOD) — dựa theo `GetFileReadAccessQuery` & `MultipartUploadService`:**
  ```csharp
  // Cấp Presigned URL: client tải trực tiếp từ S3/MinIO, server chỉ xử lý metadata
  var read = await signer.CreateAsync(new ReadUrlRequest(
      provider.Id, content.ObjectKey, file.Name, contentType, purpose, options.Lifetime), ct);
  return Result.Success(new FileReadAccessResult(read.Url, read.ExpiresAt, ...));
  ```
- **Rule Code:** `[IO]` — Severity: `WARNING` (cảnh báo định hướng kiến trúc)

---

## Checklist Rà soát (Audit Checklist)

Khi được gọi ở `--mode=audit` hoặc `--mode=fix`, kiểm tra tuần tự từng mục:

1. **`[N+1]`** Có lời gọi `db.*Async(...)` bên trong `foreach`/`for`/`Select` không? → `CRITICAL`
2. **`[ROUNDTRIP]`** Handler có query `db.Users` / `db.UserRoles` / `db.Roles` để lấy thông tin mà Actor Context đã có sẵn không? → `WARNING`
3. **`[CAS]`** Vùng mutation đồng thời (Quota, Counter, Status) có dùng Load-then-Save thay vì `ExecuteUpdateAsync` với điều kiện nguyên tử không? → `CRITICAL`
4. **`[PAGE]`** Có `Skip().Take()` trên bảng có thể phát triển lớn (> 10k bản ghi) không? Nếu có, đề xuất Keyset Cursor. → `WARNING`
5. **`[PROJ]`** Có 2+ câu `.SingleOrDefaultAsync()` / `.FirstOrDefaultAsync()` tuần tự trên các bảng liên quan không? → Gom thành LINQ JOIN. `WARNING`
6. **`[PROJ]`** Có `.Include()` kèm `.Select(f => new ...)` (custom projection) không? → `.Include()` bị EF Core bỏ qua; xóa bỏ để tiết kiệm Expression compile cost. `WARNING`
7. **`[PROJ]`** Có `CountAsync()` và `SumAsync()` riêng biệt trên cùng một `scope` không? → Gom thành 1 câu `GroupBy`. `WARNING`
8. **`[IO]`** Handler có nhận hoặc gửi dữ liệu nhị phân (file stream) qua RAM Web Server không? → `WARNING` (định hướng sang Presigned URL)
9. **`[ARCH]`** Handler có sử dụng `IApplicationDbContext` trực tiếp thay vì ủy thác cho Service/Reader chuyên trách không (so với pattern Thin Handler)? → Ghi chú về việc tái cấu trúc.

---

## Output Format

```text
====================================================================================
EF CORE PERFORMANCE REVIEW REPORT
File: {path} | Mode: {audit|fix|compare} | Binding: .NET 8 / EF Core 8 / MySQL Pomelo
====================================================================================

CRITICAL:
  [N+1] {path}:{line} — {evidence: tên method, vòng lặp, số queries ước tính}
  [CAS]  {path}:{line} — {evidence: tên counter/quota, loại race condition}

WARNING:
  [ROUNDTRIP] {path}:{line} — {evidence: field đã có trong context.Actor}
  [PAGE]      {path}:{line} — {evidence: ước tính số rows bỏ qua trang lớn nhất}
  [PROJ]      {path}:{line} — {evidence: số queries tuần tự, tên bảng}
  [IO]        {path}:{line} — {evidence: tên stream method}

PASSED:
  [pattern] {path}:{line} — {ghi nhận pattern tốt đã áp dụng}

REMEDIATION:
  - Rule:    {N+1|CAS|ROUNDTRIP|PAGE|PROJ|IO}
  - Pattern: {tên pattern chuẩn tham chiếu từ Knowledge Base}
  - Change:  {mô tả thay đổi cụ thể theo EF Core 8}
  - Spec/Plan impact: {artifact hoặc N/A}
====================================================================================
```

---

## Hướng dẫn Sử dụng

- **Khi dùng:** Rà soát Handler / Service trong `CleanArchCqrs.Application/Features/` và đề xuất tối ưu theo 6 pattern chuẩn.
- **Không dùng:** Không tự động sửa file ngoài `/add-execute` boundary; không sinh migration, schema change, index syntax khi chưa có approval.
- **Input:** `--file` hoặc `--feature` bắt buộc; `--mode` mặc định là `audit`.
- **Điều kiện trước:** EF Core 8 / Pomelo / MySQL đã `APPROVED` trong Architecture Profile hoặc AGENTS.md.
- **Evidence:** Report phải nêu path:line, ước tính số roundtrips hiện tại và sau tối ưu, pattern tham chiếu.
- **Dừng khi:** Thay đổi tối ưu ảnh hưởng đến data contract / SPEC (ví dụ: đổi kiểu phân trang từ Offset sang Cursor đòi hỏi thay đổi API contract) → Dừng và báo `Human decision required`.
- **Human quyết định:** Human duyệt mọi thay đổi ảnh hưởng API contract, schema hoặc SLA; code change chỉ thực hiện qua `/add-execute` đã approved.
- **Lệnh tiếp theo:**
  - `/sdd-update --feature=<slug> --artifact=spec --bump=patch --reason="optimize query"` khi fix không đổi contract.
  - `/sdd-review --target=<report> --status=APPROVED` trước khi apply fix ảnh hưởng contract.

---

## Ví dụ Gọi

```text
# Audit một Handler cụ thể
/efcore-performance-reviewer --file=src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs --mode=audit

# Đề xuất fix cụ thể
/efcore-performance-reviewer --file=src/CleanArchCqrs.Application/Features/Files/Queries/SearchFiles/SearchFilesQueryHandler.cs --mode=fix

# So sánh Handler chưa tối ưu với file chuẩn tốt trong cùng codebase
/efcore-performance-reviewer \
  --file=src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs \
  --against=src/CleanArchCqrs.Application/Features/Files/Queries/GetFileText/GetFileTextQuery.cs \
  --mode=compare

# Audit toàn bộ feature
/efcore-performance-reviewer --feature=files --mode=audit
```

---

## Completion Output

Sau khi xuất `EF CORE PERFORMANCE REVIEW REPORT`, tổng hợp:
- Số lượng `CRITICAL` và `WARNING` tìm thấy.
- Tổng số roundtrips DB hiện tại → số roundtrips sau khi áp dụng tất cả đề xuất (ước tính).
- Pattern chuẩn nào đã được áp dụng tốt trong file (ghi nhận `PASSED`).
- Nếu không có finding mới, chọn `continue` và chỉ theo persisted feature/delivery Follow-up hiện có.
- Nếu có `CRITICAL` hoặc impact đến contract: `BLOCKED — Human decision required`.
