# BẰNG CHỨNG TEST THỰC TẾ (Live Penetration Test)

- Thời gian: 10/10/2026
- Build: `dotnet build -c Release` → **0 Error, 3 Warning**
- Server chạy thử: `http://localhost:5299` (Hosting environment = **Production**)
- DB: MySQL localhost, DB `CloudDmsDb` (cùng connection string với appsettings)
- **Toàn bộ request dưới đây KHÔNG kèm header `Authorization`.**

> Mục đích: kiểm chứng giả thiết "gần như mọi endpoint đều không yêu cầu xác thực".
> Kết luận từng mục dựa trên **mã HTTP thực tế** trả về.

---

## 1. ❌ CRITICAL — Liệt kê toàn bộ user (có PII) không cần token

```
GET /api/admin/users
```
```
HTTP 200
{"value":{"items":[{"id":"f1be28e5-...","email":"huynthe180817@fpt.edu.vn",
"firstName":"huy","lastName":"nguyen","phoneNumber":"0333639880","isActive":true,
"roles":["TenantAdmin"],"permissions":["user.create","tenant.manage",...
```

**Đánh giá:** Nếu có `[Authorize]` hoặc FallbackPolicy, endpoint này phải trả **401**.
Kết quả 200 = không có bất kỳ kiểm soát xác thực nào. Lộ email, SĐT, role, permission của mọi TenantAdmin.

---

## 2. ❌ CRITICAL — Lấy hồ sơ chi tiết 1 user không cần token

```
GET /api/admin/users/3b7b102b-9d00-4851-932e-fd2043da3752
```
```
HTTP 200
{"value":{"id":"3b7b102b-...","email":"user@acme.com","fullName":"Alex Mercer",
"phoneNumber":"+84 914 829 901","roles":["Employee"],"permissions":[...],
"tenantId":"956000bf-...","tenantName":"Acme Corporation",
"departmentName":"Information Technology", ... }}
```

**Đánh giá:** PII đầy đủ + cấu trúc phân quyền của user bất kỳ = IDOR/Broken Access Control cấp hệ thống.

---

## 3. ❌ CRITICAL — Đọc danh sách thành viên + email + quota phòng ban không cần token

```
GET /api/department/76c20059-69d2-4de9-888e-94fd65bb8a30/quota/users
```
```
HTTP 200
{"departmentName":"Information Technology",
 "users":[{"email":"user@acme.com",...},{"email":"tenant@acme.com",...},
          {"email":"deptadmin@acme.com",...}]}
```

**Đánh giá:** Leaks toàn bộ roster + email của phòng ban cho người lạ. Route chỉ cần đoán GUID.

---

## 4. ❌ CRITICAL — Endpoint **ghi** dữ liệu chạy mà không qua 401

Mỗi lệnh dưới đây đều **không trả 401** — nghĩa là request đi thẳng vào handler:

| # | Request (không token) | Kết quả | Ý nghĩa |
|---|---|---|---|
| 4.1 | `POST /api/tenant/{guid}/department-admins` (email đã tồn tại) | **400** `{"error":"Tenant ID is required."}` | Validator/Handler chạy. Nếu payload hợp lệ → **tạo được tài khoản ẩn danh**. |
| 4.2 | `POST /api/admin/users/{id}/reset-password` | **400** `{"error":"User with ID '...' was not found."}` | Handler reset-password chạy ẩn danh (id sai nên chưa đổi pass; id đúng = đổi được mật khẩu bất kỳ ai). |
| 4.3 | `DELETE /api/files/{id}` | **500** `MySqlException: Unknown column 'f.DepartmentId'` | Handler xóa file chạy ẩn danh, lỗi là do schema, KHÔNG phải do auth. |
| 4.4 | `PUT /api/department/{deptId}/quota/users/{userId}` | **500** `FK AuditLogs_Users_UserId` (vì `UserId = Guid.Empty` khi không có token) | Handler cấp quota chạy ẩn danh. |
| 4.5 | `GET /api/admin/storage-providers` | (đọc credential đã mã hóa/kiểm soát hạ tầng) | Endpoint SystemAdmin, không auth. |

**Đánh giá:** Chưa từng nhận về **bất kỳ phản hồi 401/403 nào** trong toàn bộ session test.

---

## 5. ❌ CRITICAL — Upload file ẩn danh, giả mạo user khác (impersonation)

Code `UploadFileCommandHandler` có 2 tầng fallback:

```csharp
var userId = request.UserIdOverride ?? _currentUserService.UserId;
...
// Nếu không lấy được user từ token nhưng có TenantId → lấy user active đầu tiên của tenant
// "Fallback an toàn cuối cùng": Lấy user active hợp lệ đầu tiên trong CSDL
if (user == null) { user = await _context.Users.FirstOrDefaultAsync(u => u.IsActive); }
```

### 5a. Không token, chỉ truyền `tenantId`:
```
POST /api/files/upload   -F tenantId=956000bf-...  -F files=@probe.txt
```
```
HTTP 400
{"error":"Tài khoản chưa được phân bổ hạn mức lưu trữ (Quota cá nhân)...
```

**Đánh giá:** Request **đã đi qua** bước xác định user (fallback) → load Tenant → load Department → và **dừng ở bước kiểm tra quota**, KHÔNG dừng ở bước xác thực. Lỗi trả về do dữ liệu demo không có quota, **không phải do rào cản bảo mật**.

### 5b. Không token, truyền `userId` (giả mạo 1 user cụ thể):
```
POST /api/files/upload   -F userId=3b7b102b-...  -F files=@probe.txt
```
```
HTTP 400  {"error":"Tài khoản chưa được phân bổ hạn mức lưu trữ..."}
```
Cùng kết quả — handler đã nhận diện "user" theo giá trị do client gửi.

**Kịch bản khai thác thực tế:** với user đã có quota > 0 (bất kỳ user nào được tạo qua
`POST /api/admin/users` — handler này *tự* tạo UserStorageQuota), attacker chạy:
```
PUT  /api/department/{deptId}/quota/users/{userId}   → cấp quota  (ẩn danh)
POST /api/files/upload -F userId={userId}            → upload file tính vào quota của nạn nhân
```
→ **ghi ẩn danh + upload dưới danh nghĩa người khác, không cần bất kỳ token nào.**

---

## 6. ❌ HIGH — Môi trường thật đang vỡ chức năng vì lệch schema (migration hỏng)

Log khởi động:
```
MySqlConnector.MySqlException: Table 'Permissions' already exists
   at ... Database.MigrateAsync()   → BẮT Exception rồi nuốt ("migration skipped")
```
→ các `ALTER TABLE` raw SQL trong `Program.cs` chạy tiếp, **nhưng không có dòng nào thêm
`FileEntries.DepartmentId`** (chỉ nằm trong migration `InitialCreate`, mà migration đó không chạy được).

Khi gọi API file:
```
DELETE /api/files/{id}  →  HTTP 500
MySqlConnector.MySqlException (0x80004005): Unknown column 'f.DepartmentId' in 'field list'
   at DeleteFileCommandHandler.Handle(...) line 29
```

**Đánh giá:** Toàn bộ module File (list/delete/download) trả **500** trên DB này.
Đây là hậu quả trực tiếp của việc trộn `MigrateAsync()` + `ALTER TABLE` raw + `catch {}` nuốt lỗi.

---

## 7. Build & cảnh báo

```
Build succeeded.  0 Error(s)  3 Warning(s)
- CS8604: LoginCommandHandler.cs(64,60) 'role' có thể null → GenerateAccessToken(string role)
- CS0618 x2: GoogleCredential.FromJson(string) obsolete – "potential security risk"
```

## 8. Những thứ KHÔNG tồn tại trong repo (kiểm tra bằng file/glob)

- `detected_test_files: 0` — **không có bất kỳ test nào** (unit/integration)
- Không có `.github/workflows` — **không có CI**
- Không có `Dockerfile` / `docker-compose`
- Không có thư mục frontend nào (chỉ có `src/` backend + `docs/`)
- Không tìm thấy `MaxRequestBodySize` / `RequestSizeLimit` → không giới hạn dung lượng upload
- Không tìm thấy `UseMiddleware(GlobalExceptionHandler...)`, `UseExceptionHandler`, rate limiting

## 9. Dọn dẹp sau test

- Server đã dừng (PID 16544 terminated)
- File probe tạm (`review_probe.txt`, `up.out`, `up2.out`) đã xoá
- Không có dữ liệu nào bị thay đổi thành công (các lệnh ghi đều fail trước khi commit do lỗi FK/schema)
