# PHASE 3 — 🟡 VAN HANH (5–7 ngày)

> Mục tiêu: hệ thống vận hành được lâu dài — chống abuse, tài khoản ổn định, API ổn định, tài liệu đúng.
> **Phụ thuộc:** Phase 1 + 2.

---

## P3-01 — Rate limit + chống brute force

**Bug:** BUG-17

### Sửa
`Program.cs`:
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5; opt.Window = TimeSpan.FromMinutes(1);
    });
    options.AddFixedWindowLimiter("upload", opt =>
    {
        opt.PermitLimit = 30; opt.Window = TimeSpan.FromMinutes(1);
    });
});
app.UseRateLimiter();
```
- Gắn `[EnableRateLimiting("auth")]` cho `AuthController.Login` + `AcceptInvitation`
- Gắn `[EnableRateLimiting("upload")]` cho `FilesController.Upload`
- Theo IP **và** theo email (partition by claim/body) — ASP.NET Core `PartitioningKey`
- Ghi log login thất bại liên tiếp (5 lần → khoá 15', tạm thời dùng in-memory; Phase 4 cân nhắc DB)

### Nghiệm thu
- 6 lần login sai trong 1 phút → lần thứ 6 trả **429**
- `upload` lần thứ 31 trong 1 phút → **429**

---

## P3-02 — Refresh token + thu hồi JWT

**Bug:** BUG-18, BUG-19 · **Tài nguyên sẵn có:** bảng `RefreshTokens` + entity + index đã tồn tại (chưa ai dùng)

### Sửa
1. `AuthController` + `LoginCommandHandler`: sau khi sinh access token, tạo `RefreshToken` (token hash SHA-256, TTL 7 ngày, `CreatedByIp`) → trả trong response
2. Thêm `POST /api/auth/refresh`:
   - validate refresh token (chưa hết hạn, chưa bị thu hồi, khớp user)
   - **xoay vòng** (rotate): đánh dấu token cũ `RevokedAt`, tạo token mới → chống replay
   - revoke toàn bộ refresh token của user nếu nhận lại token đã bị revoke (phát hiện ăn cắp)
3. Thu hồi JWT khi:
   - `ChangeUserStatus(isActive=false)` → revoke mọi refresh token của user
   - `ResetUserPassword` / user tự đổi pass → revoke mọi refresh token
   - ở login check: nếu user `!IsActive` → **401** (hiện chỉ check lúc login, token cũ vẫn sống tới 60')
4. **Cách chặn token cũ sống mãi** (chọn 1): (a) rút `ExpirationMinutes` xuống 15' + bắt buộc refresh; (b) blacklist `jti` trong cache khi revoke — (a) đơn giản hơn, đề xuất.

### Nghiệm thu
- Access token hết hạn → `POST /auth/refresh` → token mới, không cần nhập lại mật khẩu
- Admin vô hiệu hoá user → request tiếp theo với token cũ → **401** (sau khi rút ngắn TTL + revoke refresh)

---

## P3-03 — Phân trang, giới hạn pageSize, index

**Bug:** BUG-27, BUG-28, BUG-36 · **P:** P1, P2, P3, P4, P10

### Sửa
1. `GetTenantFilesQuery`: thêm `PageNumber`, `PageSize`, `SearchTerm`, `OrderBy` — dùng `PagedResult<T>` (đã có sẵn trong `Common/Models`)
2. `GetUsersWithPaginationQueryHandler`: cap `pageSize`:
   ```csharp
   pageSize = Math.Clamp(request.PageSize <= 0 ? 10 : request.PageSize, 1, 100);
   ```
3. `GetDepartmentsQueryHandler`: thay vì `Include(Users).ThenInclude(UserRoles)...`, dùng projection đếm:
   ```csharp
   .Select(d => new DepartmentDto { ..., HeadCount = d.Users.Count(u => u.IsActive), Lead = ... })
   ```
   → tránh cartesian + cảnh báo `MultipleCollectionIncludeWarning` (đã thấy trong log thật)
4. Search sargable: dùng `EF.Functions.Like` thay `.ToLower().Contains()` (hoặc thêm generated column + index cho Email)
5. **Index** (migration mới):
   ```csharp
   builder.HasIndex(f => new { f.TenantId, f.IsDeleted, f.CreatedAt });   // FileEntries
   builder.HasIndex(a => new { a.TenantId, a.CreatedAt });                // AuditLogs
   builder.HasIndex(d => new { d.TenantId, d.Code }).IsUnique();          // Departments
   ```
6. `AsSplitQuery()` cho mọi query có >1 collection Include

### Nghiệm thu
- `GET /api/files?pageSize=10000` →pageSize trả về ≤ 100
- `EXPLAIN` query list file dùng index `IX_FileEntries_TenantId_IsDeleted_CreatedAt`
- Không còn warning `MultipleCollectionIncludeWarning` trong log

---

## P3-04 — Đồng bộ docs ↔ API thật

**Bug:** BUG-40 một phần · **Vấn đề:** `docs/` mô tả endpoint không tồn tại

### Sửa
Chọn **1 hướng** (đề xuất: sửa docs cho khớp code + bổ sung endpoint còn thiếu):

1. Rà từng dòng trong `docs/00…04` đối chiếu route thật:

| Docs (sai) | Thật |
|---|---|
| `/api/systemadmin/users` | `/api/admin/users` |
| `/api/systemadmin/tenants/{id}/storage-quota` | `/api/admin/tenants/{id}/quota` |
| `/api/tenantadmin/department-admins` | `/api/tenant/{tenantId}/department-admins` |
| `/api/tenantadmin/departments/{id}/storage-quota` | `/api/tenant/{tenantId}/departments/{departmentId}/quota` |
| `/api/departmentadmin/setup-department` | `/api/department/setup` |
| `/api/departmentadmin/users/{id}/storage-quota` | `/api/department/{departmentId}/quota/users/{userId}` |

2. Bổ sung endpoint còn thiếu (BUG-F1/F5): `GET /api/admin/audit-logs`, `GET /api/admin/tenants/{id}/storage-stats`, `GET /api/department/{id}/stats`
3. Sửa README (BUG-40): đúng .NET 8, đúng domain Cloud-DMS, bỏ mọi link Patreon/marketing của template

### Nghiệm thu
- Mọi endpoint trong `docs/` → gọi được (không 404)
- `README.md` không còn nhắc "Product CRUD", ".NET 10", "Patreon"

---

## P3-05 — ClamAV fail-closed + email outbox + ngừng log token

**Bug:** BUG-24, BUG-29, BUG-30, BUG-31

### Sửa
1. **`Services/ClamAvVirusScannerService.cs`:**
   - `appsettings.json` thêm `"ClamAV": { "Enabled": true, ... }`
   - **fail-closed** ở Production: nếu daemon không kết nối được → **chặn upload** (`return Infected("SCAN_UNAVAILABLE")` hoặc trả lỗi riêng), không `return Clean()`
   - giữ fail-open **chỉ** ở Development (theo `IHostEnvironment`)
2. **`Services/EmailService.cs`:**
   - **Không log activation link/token** (hiện log ở mức `Information`) → chỉ log "đã gửi mail tới {email}"
   - Không nuốt exception im lặng: ném lên để handler quyết định, hoặc ghi bảng `EmailOutbox` rồi retry bằng background job
   - Response của `CreateUser`/`CreateDepartmentAdmin` nên kèm cờ `emailQueued` để UI cảnh báo
3. **HTML encode** mọi biến chèn vào email (`recipientName`, `tenantName`, `departmentName`) — dùng `System.Net.WebUtility.HtmlEncode`

### Nghiệm thu
- `grep -rn "ACTIVATION LINK" src/` → 0 kết quả
- Dừng ClamAV daemon ở Production → upload trả **lỗi**, không im lặng cho qua
- Tạo user với `firstName = <b>x</b>` → email nhận được hiển thị chữ thường, không render HTML

---

## P3-06 — Bổ sung chức năng còn thiếu theo docs

**Bug:** BUG-F1, F2, F5, F6, F7 (xem review mục 4)

### Sửa (ưu tiên theo docs)
1. `GET /api/admin/audit-logs` — phân trang, filter theo `TenantId/Action/DateRange` (bảng `AuditLogs` đã có, chỉ thiếu endpoint)
2. `GET /api/admin/tenants/{id}/storage-stats`, `GET /api/department/{id}/stats` — tổng hợp nhanh từ 4 bảng quota
3. **Folder CRUD**: `POST/PUT/DELETE /api/folders` — hiện chỉ tạo 1 folder gốc trong `SetupDepartment`, quyền `folder.*` vô dụng
4. **Quyết định về `FileShare` + `RefreshToken`:** nếu không làm → xóa entity/table (dọn trong Phase 4); nếu làm → endpoint `POST /api/files/{id}/shares`

> Nếu **không** làm tính năng nào trong danh sách này → **bắt buộc cập nhật `docs/`** bỏ chúng, tránh docs hứa feature không có.

### Nghiệm thu
- Mỗi endpoint docs liệt kê → tồn tại và trả đúng cấu trúc

---

## P3-07 — CORS / HTTPS / Security headers

**Bug:** BUG-38 · **P:** S14

### Sửa
`Program.cs`:
```csharp
options.AddPolicy("AllowFrontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? throw new InvalidOperationException("Chưa cấu hình Cors:AllowedOrigins"))
    .AllowAnyMethod().AllowAnyHeader());
```
- Bỏ `AllowAnyOrigin()` (config `Cors:AllowedOrigins` hiện là **config chết**)
- Thêm: `app.UseHttpsRedirection();`, `app.UseHsts();`
- Thêm security headers: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Content-Security-Policy`
- Đặt Swagger **chỉ** Development (đã đúng) — nhưng cân nhắc bật OpenAPI JSON bảo mật ở staging cho team

### Nghiệm thu
- Origin `http://evil.com` → CORS chặn (response không có header `Access-Control-Allow-Origin`)
- Response có header `X-Content-Type-Options: nosniff`

---

## P3-08 — Ngừng seed tài khoản demo ở Production

**Bug:** BUG-25 · **Hiện tại:** `DbInitializer.SeedTenantsAndUsersAsync` **luôn chạy**, tạo `admin@clouddms.com / Admin@123`

### Sửa
```csharp
if (env.IsDevelopment())
{
    await DbInitializer.SeedDefaultDataAsync(...);
}
```
- Ở Production chỉ seed **RBAC** (permissions/roles — cần cho hệ thống hoạt động), **không** seed user/password
- Nếu môi trường hiện tại đã có user demo → vô hiệu hoá hoặc đổi mật khẩu thật

### Nghiệm thu
- Chạy Production → không còn tài khoản `Admin@123`
- `grep -rn "Admin@123" src/` → chỉ xuất hiện trong nhánh `IsDevelopment()`

---

## ✅ CHECKPOINT SAU PHASE 3

- [ ] `dotnet build -c Release` → 0 error
- [ ] Login 6 lần sai → 429
- [ ] Refresh token hoạt động, revoke khi deactivate
- [ ] `GET /api/files?pageSize=10000` bị cap
- [ ] Docs gọi được 100% endpoint
- [ ] Không còn log token trong file log
- [ ] Production không còn tài khoản demo
