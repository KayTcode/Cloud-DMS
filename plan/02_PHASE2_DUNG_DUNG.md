# PHASE 2 — 🟠 CHAY DUNG (4–6 ngày)

> Mục tiêu: hệ thống chạy **đúng** — DB đồng bộ, quota không sai, không race, không 500 vì logic.
> **Phụ thuộc:** Phase 1 hoàn tất (cần auth để test đúng hành vi).

---

## P2-01 — Sửa migration: một nguồn sự thật duy nhất

**Bug:** BUG-08 · **Đã chứng minh:** DB thật đang thiếu `FileEntries.DepartmentId` → mọi thao tác file = 500

### Sửa
1. **Backup trước:** `mysqldump CloudDmsDb > backup_before_migration.sql`
2. `Program.cs` — **xóa toàn bộ** (~150 dòng):
   - `ALTER TABLE ... ADD COLUMN ...` (6 khối)
   - `CREATE TABLE IF NOT EXISTS ...` (3 khối)
   - `UPDATE Users SET EmailConfirmed = 1 WHERE EmailConfirmed = 0` ← **bug riêng**: tự động xác nhận mọi user khi restart, vô hiệu hoá xác thực email
   - các `catch { }` nuốt lỗi
3. Giữ lại **duy nhất** `await context.Database.MigrateAsync();` trong try/catch — nhưng catch phải `logger.LogError` và **ném lại** ở môi trường Production (không nuốt im lặng).
4. Đồng bộ migration với model hiện tại: `dotnet ef migrations add FinalSync` để tạo migration bổ sung các cột thiếu (`FileEntries.DepartmentId`, `ScanStatus`, `StorageProviderId`, `Tenants.StorageUsedBytes`, `Departments.StorageQuotaBytes/StorageUsedBytes`, `StorageProviders`, `UserStorageQuotas`, `EmailVerificationTokens`...), hoặc nếu DB dev chưa quan trọng → drop + `MigrateAsync` lại từ đầu.
5. Thêm bước verify sau migrate: chạy 1 query kiểm tra `SELECT 1 FROM FileEntries LIMIT 1` và log kết quả.

### Nghiệm thu
- DB sạch → `dotnet ef database update` chạy hết, **không còn ALTER raw**
- `DELETE /api/files/{id}` không còn `Unknown column 'f.DepartmentId'`
- Restart server 2 lần → schema không đổi, không còn log `ALTER TABLE ... failed`

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-F (Database)**

---

## P2-02 — Gộp cấu hình EF trùng lặp (2 folder)

**Bug:** BUG-15 · **Số file trùng:** 9 cặp (User, Tenant, Role, Department, Permission, RefreshToken, AuditLog, UserRole, RolePermission)

### Sửa
1. Xác định bên "đúng" (bản đầy đủ hơn là `Configurations/` — có `HasDefaultValue`, `HasMaxLength` chi tiết): diff từng cặp
2. Xoá **toàn bộ** folder `src/CleanArchCqrs.Infrastructure/Persistence/Configuations/` (đáng lẽ phải là "Configurations" — gõ sai chính tả)
3. Chuyển phần còn thiếu (nếu có) sang `Configurations/`
4. `AppDbContext.OnModelCreating` giữ nguyên `ApplyConfigurationsFromAssembly` — nhưng giờ chỉ còn 1 cấu hình/entity

### Nghiệm thu
- `dotnet ef migrations has-pending-model-changes` → không có thay đổi model ngoài ý muốn (hoặc nếu có → tạo migration)
- `grep -r "namespace.*Configuations" src/` → 0 kết quả

---

## P2-03 — Xóa file phải hoàn quota phòng ban

**Bug:** BUG-09 · **Hậu quả:** `Department.StorageUsedBytes` chỉ tăng không giảm → phòng ban hết quota vĩnh viễn

### Sửa
`Features/Files/Commands/DeleteFile/DeleteFileCommand.cs` — sau khi hoàn quota tenant/provider/user, thêm:

```csharp
if (fileEntry.DepartmentId.HasValue)
{
    var department = await _context.Departments
        .FirstOrDefaultAsync(d => d.Id == fileEntry.DepartmentId.Value, ct);
    if (department != null)
        department.StorageUsedBytes = Math.Max(0, department.StorageUsedBytes - fileEntry.SizeBytes);
}
```

**Song song đó:** viết script đối soát 1 lần (so `SUM(FileEntries.SizeBytes)` theo Tenant/Department/User với giá trị trong các bảng quota) vì số liệu cũ đã bị lệch.

### Nghiệm thu
- Upload 1 file → `Department.StorageUsedBytes` tăng đúng; xóa file → **trở về đúng giá trị ban đầu**
- `Tenant.StorageUsedBytes`, `UserStorageQuota.UsedBytes`, `StorageProvider.UsedCapacityBytes` cũng quay về

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-G (Quota)**

---

## P2-04 — Ngăn quota overcommit khi tạo user

**Bug:** BUG-10 · **Vị trí:** `Features/SystemAdmin/Commands/CreateUser/CreateUserCommandHandler.cs` bước 4

### Sửa
Hiện: `userQuotaBytes = tenant.StorageQuotaBytes > 0 ? tenant.StorageQuotaBytes : 10GB` → 10 user = 100GB quota trên tenant 10GB.

Thay bằng: lấy **còn trống** của tenant:
```csharp
var allocated = await _context.UserStorageQuotas
    .Where(q => q.User.TenantId == tenant.Id)
    .SumAsync(q => q.QuotaBytes, ct);
var granted = await _context.Departments
    .Where(d => d.TenantId == tenant.Id)
    .SumAsync(d => d.StorageQuotaBytes, ct);
var available = Math.Max(0, tenant.StorageQuotaBytes - allocated - granted);
long userQuotaBytes = Math.Min(request.InitialQuotaBytes ?? 1GB, available);
if (userQuotaBytes <= 0) → Failure("Tenant đã hết hạn mức khả dụng")
```
Thêm `InitialQuotaBytes` (optional, default 1GB) vào `CreateUserCommand`.

### Nghiệm thu
- Tạo 11 user, mỗi user 1GB, tenant quota 10GB → user thứ 11 **bị chặn** với thông báo rõ

---

## P2-05 — Sửa double-book quota ở CreateDepartmentAdmin

**Bug:** BUG-11 · **Vị trí:** `Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs`

### Sửa
Vấn đề: `department.StorageQuotaBytes = quotaToAllocate` (ghi đè, không cộng) **và** đồng thời tạo ví `UserStorageQuota` bằng đúng số đó → 2 lần tính vào cùng 1 nguồn.

Chọn **một** mô hình nhất quán (đề xuất — theo đúng docs 00):
- Nếu truyền `DepartmentId` (gắn vào phòng sẵn): **cộng dồn** `department.StorageQuotaBytes += quotaToAllocate;` và **không** tạo ví `UserStorageQuota` cho admin (hoặc ví = 0).
- Nếu chưa có phòng (self-setup): chỉ tạo ví `UserStorageQuota`, KHÔNG chạm `department` nào.
- Kiểm tra khả dụng đã tính (`existingDeptsQuota + pendingAdminsQuota`) phải tính **sau khi** quyết định mô hình, tránh lệch.

Thêm 2 dòng check: `quotaToAllocate` không vượt khả dụng (đã có) **và** sau khi cấp phải `SaveChanges` trong transaction với check lại.

### Nghiệm thu
- Tạo DeptAdmin gắn phòng sẵn 2 lần với 1GB → quota phòng tăng đúng +1GB mỗi lần (không bị ghi đè), tổng không vượt tenant quota
- Tạo DeptAdmin chờ setup → tổng ví + tổng phòng ≤ tenant quota

---

## P2-06 — Transaction + chống race cho mọi thao tác quota

**Bug:** BUG-20, BUG-35 · **Bản chất:** check-then-act không lock → 2 request đồng thời cùng vượt quota

### Sửa
Chọn 1 trong 2 cách (đề xuất A cho MySQL):

**A. pessimistic lock (đơn giản, chắc chắn):**
```csharp
using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
// ... check quota ...
await _context.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```
Áp dụng cho: `UploadFileCommandHandler`, `DeleteFileCommandHandler`, `AllocateUserStorageQuota`, `AllocateDepartmentStorageQuota`, `AllocateTenantStorageQuota`, `CreateDepartmentAdmin`, `SetupDepartment`.

**B. optimistic concurrency:** thêm cột `RowVersion`/`Version` vào `UserStorageQuota`, `Department`, `Tenant`, `StorageProvider`; EF `IsRowVersion()` → `DbUpdateConcurrencyException` → trả 409 "Dữ liệu vừa được thay đổi, thử lại".

Tương tự cho **accept-invitation** (BUG-35): đánh dấu `IsUsed` rồi `SaveChanges` trong transaction, hoặc `UPDATE ... SET IsUsed=1 WHERE Id=@id AND IsUsed=0` và check affected rows == 1.

> Lưu ý: `EnableRetryOnFailure(5)` đang bật — retry **+ transaction** cần đưa logic vào `ExecutionStrategy.ExecuteAsync` để tránh lỗi "transactions cannot be created while retries enabled". Đây là việc bắt buộc khi sửa.

### Nghiệm thu
- Test concurrency: chạy **20 request upload đồng thời** cho file 600MB vào user quota 1GB → chỉ **1 file** thành công, phần còn lại 400, tổng UsedBytes không vượt 1GB
- 2 request accept-invitation cùng token đồng thời → đúng 1 request 200, request kia 400

### Kiểm chứng
`06_CHECKLIST_KIEM_NGHIEM.md` → **CL-H (Concurrency)**

---

## P2-07 — Gán đúng role khi tạo user + bổ sung luồng Employee

**Bug:** BUG-12, BUG-26 · **Thiếu requirement:** docs 03 yêu cầu `POST /api/departmentadmin/employees` — không tồn tại

### Sửa
1. `CreateUserCommandHandler` (Features/SystemAdmin): **dùng `request.RoleIds`** thay vì gán cứng `TenantAdmin`:
   ```csharp
   var roles = await _context.Roles.Where(r => request.RoleIds.Contains(r.Id)).ToListAsync(ct);
   if (roles.Count == 0) → gán mặc định theo ngữ cảnh (vd: có DepartmentId → Employee, không → TenantAdmin)
   foreach (var r in roles) user.UserRoles.Add(new UserRole(...));
   ```
   Validate: role phải tồn tại; `SystemAdmin` chỉ SystemAdmin mới được gán (so `_currentUserService.Role`); không cho gán chính mình lên cao hơn.
2. Thêm endpoint `POST /api/department/{departmentId}/employees` (DepartmentAdmin): tạo user role `Employee`, gắn `TenantId`/`DepartmentId` của caller, cấp quota ban đầu từ quota phòng còn trống, gửi invitation email.
3. Sửa `GetUsersWithPaginationQueryHandler` (BUG-26): bỏ `.Where(u => u.UserRoles.Any(ur => ur.Role.Name == "TenantAdmin"))` — thay bằng:
   - SystemAdmin → xem tất cả
   - TenantAdmin → `u.TenantId == currentUser.TenantId`
   - DepartmentAdmin → `u.DepartmentId == currentUser.DepartmentId`

### Nghiệm thu
- `POST /api/admin/users` với `roleIds=[Employee]` → user có role Employee (không còn TenantAdmin)
- DepartmentAdmin tạo được employee qua endpoint mới
- `GET /api/admin/users` trả về cả employee (khi caller SystemAdmin) — đúng docs "tất cả Users"

---

## P2-08 — Sửa 2 lỗi 500: AuditLog FK & DeleteUser FK

**Bug:** BUG-22, BUG-23 · **Đã chứng minh thật:** `FK_AuditLogs_Users_UserId` fails khi caller không có claim

### Sửa
1. `AllocateUserStorageQuotaCommand` + `AllocateDepartmentStorageQuotaCommand`:
   ```csharp
   UserId = _currentUserService.UserId ?? Guid.Empty   // ← Guid.Empty vi phạm FK
   ```
   → Sau Phase 1, `UserId` không còn null (đã 401 nếu chưa đăng nhập). Nhưng vẫn đổi an toàn:
   ```csharp
   UserId = _currentUserService.UserId
            ?? throw new UnauthorizedAccessException("Thiếu danh tính người gọi.");
   ```
   Hoặc cho cột `AuditLogs.UserId` nullable trong migration.

2. `DeleteUserCommandHandler` — hard-delete va `FileEntries.OwnerId` FK `Restrict`:
   - **Chọn chiến lược:** (a) soft-delete (`IsActive=false`) + chặn login, hoặc (b) vẫn hard-delete nhưng kiểm tra trước:
   ```csharp
   var hasFiles = await _context.FileEntries.AnyAsync(f => f.OwnerId == user.Id && !f.IsDeleted, ct);
   if (hasFiles) return Failure("User còn file sở hữu — không thể xóa. Hãy vô hiệu hoá thay vì xóa.");
   ```
   Đề xuất **(a) soft-delete** cho production (giữ audit trail).

### Nghiệm thu
- Gọi cấp quota có token hợp lệ → không còn 500 FK
- `DELETE /api/admin/users/{id}` có file → trả message rõ (400), không 500

---

## P2-09 — Upload nhiều file: không để trạng thái nửa vời

**Bug:** BUG-21 · **Vị trí:** `FilesController.Upload` loop gọi `Sender.Send` từng file

### Sửa
3 phương án (chọn 1):
- **A (đơn giản):** Nếu 1 file fail → trả về danh sách `uploaded[]` + `failed[{name, error}]` kèm HTTP 207/200 để client biết file nào đã vào (không im lặng mất dữ liệu).
- **B (đúng nhất):** Tách "kiểm tra quota tổng thể trước" (tổng size các file) → nếu vượt → chặn ngay, không upload file nào.
- **C:** có endpoint `DELETE` bù nếu client hủy.

Đề xuất **B** (pre-check tổng) + **A** (response rõ ràng).

### Nghiệm thu
- Upload 3 file vượt quota → **không file nào** được lưu, quota không đổi, response nêu rõ lý do

---

## P2-10 — Hợp nhất 2 bộ code trùng lặp

**Bug:** BUG-39, BUG-34, phần BUG-15 · **Vị trí:** `Application/Users/*` vs `Application/Features/*`

### Sửa
1. `Users/Commands/CreateUser/*` (trả `Guid`, không gán role) **trùng** với `Features/SystemAdmin/Commands/CreateUser/*` (trả `Result<UserDto>`) → **giữ bản Features**, xóa bản Users.
2. `Users/Commands/UpdateUser`, `Users/Commands/DeactiveUser` (class `internal`, không controller dùng) → **xóa**.
3. `UserController`:
   - `POST login` trùng `AuthController.login` → xóa, chỉ giữ `AuthController`
   - `GET {id}` trả `Ok()` rỗng → xóa hoặc trả dữ liệu thật
   - `POST` (CreateUser) → chuyển sang `Admin/UsersController`
   → Sau đó `UserController` có thể **xóa hẳn**.
4. `AcceptInvitationCommandHandler` — bổ sung tạo `UserStorageQuota` nếu chưa có (BUG-34, theo docs 02 Bước 3):
   ```csharp
   if (!await _context.UserStorageQuotas.AnyAsync(q => q.UserId == user.Id, ct) && user.Tenant != null)
       await _context.UserStorageQuotas.AddAsync(new UserStorageQuota { UserId = user.Id, QuotaBytes = 1GB, UsedBytes = 0 }, ct);
   ```
   (Lưu ý: dùng "còn trống của tenant", không phải `tenant.StorageQuotaBytes` — tránh lặp lại BUG-10.)
5. Xóa 1 trong 2 `AddInfrastructureServices` (bản nhận `string?` hard-code `root/root`).

### Nghiệm thu
- `grep -rn "class CreateUserCommandHandler" src/` → **1 kết quả**
- Không còn endpoint login nào ở `UserController`
- User kích hoạt qua invitation → có `UserStorageQuota` ngay, upload được

---

## ✅ CHECKPOINT SAU PHASE 2

- [ ] `dotnet build -c Release` → 0 error
- [ ] Không còn `Configuations` (typo) trong solution
- [ ] Không còn `ALTER TABLE`/`CREATE TABLE` trong `Program.cs`
- [ ] Test quota qua `CL-G`, test race qua `CL-H`
- [ ] Script đối soát quota chạy sạch (chênh lệch = 0)
