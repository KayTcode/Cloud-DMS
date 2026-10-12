# CHECKLIST KIEM NGHIEM

> Dùng để xác nhận từng nhóm fix **đã thực sự hoạt động** (không phải chỉ build xanh).
> Chạy sau khi `dotnet build -c Release` = 0 error.
> Base URL mẫu: `http://localhost:5299` — đổi theo môi trường.
> Token lấy từ `POST /api/auth/login`.

**Quy tắc:** dán kết quả thật (mã HTTP + body rút gọn) vào PR/commit. Không đánh dấu `VERIFIED` nếu chưa có kết quả.

```bash
BASE=http://localhost:5299
TOKEN_SA=...    # SystemAdmin
TOKEN_TA=...    # TenantAdmin
TOKEN_EMP=...   # Employee
```

---

## CL-A — Xác thực (P1-01)

```bash
# 1. Mọi endpoint nhạy cảm PHẢI 401 khi không token
for ep in \
  "GET /api/admin/users" \
  "GET /api/files" \
  "POST /api/files/upload" \
  "GET /api/files/x/download" \
  "DELETE /api/files/x" \
  "POST /api/tenant/x/department-admins" \
  "PUT /api/admin/tenants/x/quota" \
  "POST /api/admin/users/x/reset-password" \
  "PUT /api/department/x/quota/users/y" \
  "POST /api/department/setup" ; do
  m=${ep% *}; p=${ep#* }
  printf "%-45s -> " "$m $p"
  curl -s -o /dev/null -w "%{http_code}\n" -X "$m" "$BASE$p"
done
# KỲ VỌNG: tất cả = 401
# TRƯỚC FIX: tất cả = 200/400/500 (xem review/01_TEST_EVIDENCE.md)

# 2. Endpoint public VẪN hoạt động
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/auth/login" \
  -H 'Content-Type: application/json' -d '{"email":"user@acme.com","password":"User@123"}'
# KỲ VỌNG: 200
```

---

## CL-B — Phân quyền & IDOR quota (P1-02, P1-05)

```bash
# Employee gọi admin endpoint với token hợp lệ -> 403
curl -s -o /dev/null -w "%{http_code}\n" "$BASE/api/admin/users" -H "Authorization: Bearer $TOKEN_EMP"
# KỲ VỌNG: 403

# SystemAdmin -> 200
curl -s -o /dev/null -w "%{http_code}\n" "$BASE/api/admin/users" -H "Authorization: Bearer $TOKEN_SA"
# KỲ VỌNG: 200

# DepartmentAdmin phòng X cấp quota cho user phòng Y -> chặn (403/Failure)
curl -s -X PUT "$BASE/api/department/{deptY}/quota/users/{userX}" \
  -H "Authorization: Bearer $TOKEN_DEPTADMIN" -H 'Content-Type: application/json' \
  -d '{"quotaBytes":1073741824}'
# KỲ VỌNG: 403 hoặc Result.Failure (không phải 200)
```

---

## CL-C — Upload & Path Traversal (P1-03, P1-07)

```bash
# 1. Upload không token -> 401 (trước fix: 400 ở bước quota = đã bypass auth)
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/files/upload" \
  -F "tenantId=956000bf-d6db-4613-8818-c8d216327412" \
  -F "userId=3b7b102b-9d00-4851-932e-fd2043da3752" \
  -F "files=@probe.txt;type=text/plain"
# KỲ VỌNG: 401

# 2. Upload có token hợp lệ -> 200
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/files/upload" \
  -H "Authorization: Bearer $TOKEN_EMP" -F "files=@probe.txt;type=text/plain"
# KỲ VỌNG: 200

# 3. Path traversal: tên file chứa ../../  -> phải nằm TRONG base path
curl -s -X POST "$BASE/api/files/upload" -H "Authorization: Bearer $TOKEN_EMP" \
  -F 'files=@probe.txt;filename=../../evil.txt'
# KỲ VỌNG: 200 nhưng file KHÔNG xuất hiện ngoài thư mục Storage;
#           grep log không có đường dẫn escape

# 4. File quá lớn -> 413
dd if=/dev/zero of=big.bin bs=1M count=500
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/files/upload" \
  -H "Authorization: Bearer $TOKEN_EMP" -F "files=@big.bin"
# KỲ VỌNG: 413
```

---

## CL-D — Cách ly Tenant (P1-04)

```bash
# User tenant A download file của tenant B -> 404/400, KHÔNG trả nội dung file
curl -s -o /dev/null -w "%{http_code}\n" \
  "$BASE/api/files/{idFileCuaTenantB}/download" -H "Authorization: Bearer $TOKEN_A"
# KỲ VỌNG: 404 (không phải 200)

# Employee GET /api/files -> chỉ thấy file phòng ban của mình
curl -s "$BASE/api/files" -H "Authorization: Bearer $TOKEN_EMP" | jq '.value.files[].departmentName' | sort -u
# KỲ VỌNG: chỉ 1 phòng ban (phòng của employee đó)

# Không truyền tenantId từ client được nữa
curl -s -o /dev/null -w "%{http_code}\n" "$BASE/api/files?tenantId=956000bf-..." -H "Authorization: Bearer $TOKEN_EMP"
# KỲ VỌNG: bỏ qua tenantId (kết quả vẫn là data của tenant mình) hoặc 400
```

---

## CL-E — Xử lý lỗi (P1-08)

```bash
# 1. Endpoint lỗi -> JSON có cấu trúc, KHÔNG có ex.Message/stack trace
curl -s -i -X DELETE "$BASE/api/files/11111111-2222-3333-4444-555555555555" \
  -H "Authorization: Bearer $TOKEN_EMP" | head -20
# KỲ VỌNG: 4xx với body dạng {"error":...} — không chứa "MySqlException", "at CleanArchCqrs..."

# 2. Body thiếu trường -> 400 (không 500)
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/auth/login" \
  -H 'Content-Type: application/json' -d '{}'
# KỲ VỌNG: 400

# 3. grep không còn trả ex.Message
grep -rn "ex.Message" src/CleanArchCqrs.API/Controllers/ | grep -v "//"
# KỲ VỌNG: 0 kết quả
```

---

## CL-F — Database / Migration (P2-01, P2-02)

```bash
# 1. Không còn ALTER/CREATE raw trong Program.cs
grep -nE "ALTER TABLE|CREATE TABLE|ExecuteSqlRawAsync" src/CleanArchCqrs.API/Program.cs
# KỲ VỌNG: 0 kết quả

# 2. Không còn folder Configuations (typo) và cấu hình trùng
grep -rn "namespace.*Configuations" src/ ; ls src/CleanArchCqrs.Infrastructure/Persistence/
# KỲ VỌNG: 0 match; chỉ còn 1 folder cấu hình

# 3. Migration khớp model (cần dotnet-ef)
dotnet ef migrations has-pending-model-changes -p src/CleanArchCqrs.Infrastructure
# KỲ VỌNG: false / "No changes"

# 4. Module File không còn 500 do schema lệch (test với DB dev thật)
#    (đã từng lỗi: Unknown column 'f.DepartmentId')
curl -s -o /dev/null -w "%{http_code}\n" "$BASE/api/files" -H "Authorization: Bearer $TOKEN_EMP"
# KỲ VỌNG: 200 (không 500)

# 5. Server restart 2 lần -> không còn log "ALTER TABLE ... failed" / "already exists"
```

---

## CL-G — Quota (P2-03, P2-04, P2-05)

```bash
# Chuẩn bị: ghi lại giá trị ban đầu
#   Department.StorageUsedBytes, Tenant.StorageUsedBytes, UserStorageQuota.UsedBytes

# 1. Upload 1 file -> cả 3 cấp tăng đúng size
# 2. Xóa file đó  -> cả 3 cấp QUAY VỀ ĐÚNG giá trị ban đầu
#    ⚠ BUG-09 trước fix: Department.StorageUsedBytes KHÔNG giảm
curl -s -X DELETE "$BASE/api/files/{fileId}" -H "Authorization: Bearer $TOKEN_EMP"
#    Sau đó query DB: SELECT StorageUsedBytes FROM Departments WHERE Id='{deptId}';
#    KỲ VỌNG: bằng đúng giá trị trước khi upload

# 3. Overcommit: tenant quota 10GB, tạo 11 user mỗi user 1GB
for i in $(seq 1 11); do
  curl -s -o /dev/null -w "%{http_code} " -X POST "$BASE/api/admin/users" \
    -H "Authorization: Bearer $TOKEN_SA" -H 'Content-Type: application/json' \
    -d "{\"email\":\"u$i@test.com\",\"firstName\":\"a\",\"lastName\":\"b\",\"tenantId\":\"$TENANT\",\"initialQuotaBytes\":1073741824}"
done; echo
# KỲ VỌNG: 10 lần 200, lần 11 = 400 "đã hết hạn mức khả dụng"

# 4. Tạo DeptAdmin gắn phòng 2 lần, mỗi lần 1GB -> quota phòng TĂNG CỘNG DỒN
#    (BUG-11 trước fix: lần 2 GHI ĐÈ giá trị cũ)

# 5. Đối soát tổng: chạy script so sánh
#    SUM(FileEntries.SizeBytes) theo Tenant/Department/Owner
#    với Tenant.StorageUsedBytes / Department.StorageUsedBytes / UserStorageQuota.UsedBytes
# KỲ VỌNG: chênh lệch = 0
```

---

## CL-H — Đồng thời / Race (P2-06)

```bash
# 1. 20 request upload đồng thời, quota user = 1GB, mỗi file 600MB
#    -> chỉ 1 file thành công, tổng UsedBytes KHÔNG vượt 1GB
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/files/upload" \
    -H "Authorization: Bearer $TOKEN_EMP" -F "files=@big600mb.bin" &
done; wait
# KỲ VỌNG: đúng 1×200, 19×400; UsedBytes <= 1073741824

# 2. 2 request accept-invitation cùng 1 token đồng thời
# KỲ VỌNG: đúng 1×200, 1×400 "đã được sử dụng"

# 3. Không còn lỗi "retries are not supported when execution strategy..."
```

---

## CL-J — Tạo user & Role (P2-07, P2-10)

```bash
# 1. Tạo user với roleIds=[Employee] -> role là Employee (không còn TenantAdmin)
curl -s -X POST "$BASE/api/admin/users" -H "Authorization: Bearer $TOKEN_SA" \
  -H 'Content-Type: application/json' \
  -d '{"email":"emp@test.com","firstName":"a","lastName":"b","password":"Pass@123","tenantId":"...","roleIds":["<EmployeeRoleId>"]}' | jq '.value.roles'
# KỲ VỌNG: ["Employee"]

# 2. GET /api/admin/users trả về cả employee (SystemAdmin)
curl -s "$BASE/api/admin/users?searchTerm=emp@test.com" -H "Authorization: Bearer $TOKEN_SA" | jq '.value.totalCount'
# KỲ VỌNG: >= 1  (BUG-26 trước fix: 0)

# 3. Chỉ còn 1 CreateUserCommandHandler
grep -rn "class CreateUserCommandHandler" src/
# KỲ VỌNG: 1 kết quả

# 4. UserController không còn endpoint login trùng
grep -n "HttpPost(\"login\")" src/CleanArchCqrs.API/Controllers/UserController.cs
# KỲ VỌNG: 0

# 5. User kích hoạt qua invitation có UserStorageQuota ngay
# KỲ VỌNG: SELECT * FROM UserStorageQuotas WHERE UserId=... -> 1 dòng
```

---

## CL-K — Rate limit & Token (P3-01, P3-02)

```bash
# 1. 6 lần login sai trong 1 phút
for i in $(seq 1 6); do
  curl -s -o /dev/null -w "%{http_code} " -X POST "$BASE/api/auth/login" \
    -H 'Content-Type: application/json' -d '{"email":"x@y.z","password":"wrong"}'
done; echo
# KỲ VỌNG: 5 lần 401, lần 6 = 429

# 2. Refresh token: login -> lấy refreshToken -> hết hạn access -> refresh -> dùng token mới
# KỲ VỌNG: 200 với accessToken mới

# 3. Revoke: admin deactivate user -> request kế tiếp với token cũ
# KỲ VỌNG: 401
```

---

## CL-M — Email / ClamAV (P3-05)

```bash
# 1. Không còn log token activation
grep -rn "ACTIVATION LINK" src/
# KỲ VỌNG: 0

# 2. HTML encode
#    Tạo user firstName='<b>Hacker</b>' -> mở email -> hiển thị chữ thường

# 3. ClamAV fail-closed: dừng daemon, thử upload
#    KỲ VỌNG ở Production: upload BỊ CHẶN với thông báo rõ (không phải "Clean")

# 4. Seed demo chỉ ở Development
grep -rn "Admin@123" src/ -B3 | grep -i "IsDevelopment"
# KỲ VỌNG: câu lệnh seed nằm trong nhánh IsDevelopment()
```

---

## CL-N — CORS / Headers (P3-07)

```bash
# 1. Origin lạ không được CORS
curl -s -I -X OPTIONS "$BASE/api/auth/login" -H "Origin: http://evil.com" \
  -H "Access-Control-Request-Method: POST" | grep -i access-control
# KỲ VỌNG: không có Access-Control-Allow-Origin: http://evil.com

# 2. Security headers
curl -s -I "$BASE/health" | grep -iE "x-content-type|x-frame|strict-transport"
# KỲ VỌNG: có các header này
```

---

## CL-P — Test / CI / Dọn code (Phase 4)

```bash
dotnet build -c Release          # 0 error
dotnet test                      # xanh
grep -rn "Product" src/          # 0 (trừ docs)
grep -rn "private static string FormatBytes" src/   # đúng 1
grep -rn "Repositorires" src/    # 0
grep -rn "AddInfrastructureServices" src/           # đúng 1 implementation
grep -rn "laiw yryo\|DefaultSuperSecretKey\|CloudDMS-SuperSecure" --include="*.json" --include="*.cs" src/
                                 # 0 kết quả
gitleaks detect --source .       # 0 secret mới
docker compose up -d && curl -f $BASE/health       # healthy
```

**Test FAILS khi revert fix?** Với mỗi bug Critical, đảo ngược fix (vd. bỏ lại `[Authorize]`) rồi chạy lại test tương ứng → **test phải đỏ**. Nếu test vẫn xanh nghĩa là test chưa bắt được bug.

---

## ✅ QUY TRÌNH ĐÓNG PHASE

1. Chạy toàn bộ checklist của phase đó
2. Dán kết quả (lệnh + output) vào PR
3. Cập nhật `05_BANG_THEO_DOI.md` → `VERIFIED`
4. Merge khi build 0 error + test xanh + đủ kết quả
