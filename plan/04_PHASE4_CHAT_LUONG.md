# PHASE 4 — 🟢 CHAT LUONG (4–5 ngày)

> Mục tiêu: có safety net để không tái phát, và dọn nợ kỹ thuật.
> **Có thể bắt đầu song song từ đầu** (viết test sớm tốt hơn viết test sau).

---

## P4-01 — Thêm test project

**Bug:** BUG-32, BUG-33 một phần (cần test để không regress) · **Hiện trạng:** `detected_test_files: 0`

### Sửa
1. Tạo `tests/CleanArchCqrs.Tests` (xUnit + FluentAssertions + Moq/NSubstitute):
   ```
   tests/
   ├── CleanArchCqrs.Tests/            # unit: validator, handler, domain
   └── CleanArchCqrs.IntegrationTests/ # WebApplicationFactory + Testcontainers MySQL
   ```
2. Thêm vào solution: `dotnet sln add`
3. **Test bắt buộc (tối thiểu):**

| Loại | Case |
|---|---|
| Auth | Mọi endpoint nhạy cảm trả 401 khi không token (test đúng bug BUG-01) |
| Authz | Employee gọi admin endpoint → 403 |
| IDOR | Tenant A không download/delete file của Tenant B |
| Quota boundary | Upload đúng bằng quota → OK; vượt 1 byte → bị chặn |
| Quota race | 20 task song song → tổng UsedBytes ≤ QuotaBytes |
| Quota refund | Upload → xóa → 4 bảng quay về đúng |
| Invalid | Email trùng, password < 6, quota âm, GUID rỗng |
| Permission | Gán role khác nhau → policy khác nhau |
| Token | Refresh token xoay vòng; token của user đã deactivate → 401 |
| Path traversal | Tên file `../../evil` bị sanitize |

4. `dotnet test` phải xanh

### Nghiệm thu
- `dotnet test` → pass
- **Test FAILS khi revert fix BUG-01** (chứng minh test có thật sự bắt bug)

---

## P4-02 — CI (GitHub Actions)

**Bug:** hiện không có `.github/workflows`

### Sửa
`.github/workflows/ci.yml`:
```yaml
on: [push, pull_request]
jobs:
  build-test:
    runs-on: ubuntu-latest
    services:
      mysql: { image: mysql:8.0, env: {...}, ports: [3306:3306] }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore   # fail nếu có warning? tối thiểu fail nếu error
      - run: dotnet test -c Release --no-build
      - run: dotnet ef migrations has-pending-model-changes (kiểm tra migration khớp model)
```
+ bước quét secret: `gitleaks detect --source .`

### Nghiệm thu
- PR có lỗi build/test → CI đỏ, không merge được

---

## P4-03 — Docker

**Bug:** không có Dockerfile/docker-compose (yêu cầu review mục 8/P11)

### Sửa
1. `Dockerfile` multi-stage (SDK 8 → runtime 8), chạy với user không root
2. `docker-compose.yml`: `api` + `mysql:8.0` (+ `clamav/clamav` để ClamAV bật thật)
3. Env var secrets qua `.env` (không commit `.env`)
4. Health check: `GET /health` — thêm `builder.Services.AddHealthChecks()` + `app.MapHealthChecks("/health")`

### Nghiệm thu
- `docker compose up` → `curl /health` = healthy, đăng nhập được bằng seed dev

---

## P4-04 — Dọn dead code, typo, magic number

**Bug:** BUG-32, BUG-33, BUG-37 · review mục 9 (A6, A10)

### Sửa
1. **Xóa dead code:**
   - `Domain/Entities/Product.cs`, `Application/Common/Models/ProductDto.cs`, `DbSet<Product>` (không dùng)
   - `LoggingBehavior` stub → **viết thật** (log request name + elapsed + correlation id) **và đăng ký** vào `ApplicationServiceExtensions` (hiện **không được đăng ký**)
   - `Users/Commands/UpdateUser`, `Users/Commands/DeactiveUser` (internal, không ai dùng)
   - folder `Configuations` (đã xử lý ở P2-02)
   - `DependencyInjection/InfrastructureServiceExtensions.cs` (bản `root/root`)
2. **Sửa typo:** `Common/Interfaces/Repositorires/` → `Repositories/`
3. **Gọi `MarkLogin()`** trong `LoginCommandHandler` (BUG-32 — cột `LastLoginAt` hiện luôn NULL)
4. **Trừu tượng hoá `FormatBytes`**: copy-paste ở 5+ file → `Common/Utils/ByteFormatter.cs`
5. **Hằng số quota**: `5GB/10GB/1GB/15GB` hard-code rải rác → `Domain/Common/QuotaDefaults.cs`
6. **Tách `Program.cs`**: hiện `Main` ~300 dòng (gồm cả migration + seeding) →
   `Extensions/ServiceCollectionExtensions.cs`, `Extensions/DatabaseSeedExtensions.cs`
7. Gộp `UploadFileCommandHandler` (~300 dòng, làm quá nhiều việc) → tách `QuotaService`, `FileAuditService`

### Nghiệm thu
- `grep -rn "FormatBytes" src/` → chỉ 1 định nghĩa
- `grep -rn "Product" src/` → 0 (hoặc chỉ trong file docs)
- `dotnet build` vẫn 0 error, mọi test vẫn xanh

---

## P4-05 — README đúng với dự án thật

**Bug:** BUG-40

### Sửa
README hiện là của template Patreon (.NET 10, Product CRUD, link bán hàng). Viết lại:
- Tên thật: Cloud-DMS — Enterprise Multi-Tenant DMS
- Tech stack thật: .NET 8, EF Core 8 + MySQL 8, MediatR, FluentValidation, BCrypt, MailKit, ClamAV
- Chạy local: `docker compose up` + `dotnet run`
- Bảng 4 role + cách seed tài khoản dev
- Link tới `docs/` + `review/` + `plan/`
- Xóa toàn bộ link Patreon/BuyMeACoffee/YouTube của template (hoặc ghi rõ nguồn template ở mục Credits)

### Nghiệm thu
- README không còn "Patreon", ".NET 10", "Product", "29 passing tests"

---

## P4-06 — Benchmark khả năng mở rộng (100 → 100k users)

**Yêu cầu review mục 10**

### Sửa
1. Script benchmark (NBomber/k6/artillery) cho 3 endpoint nóng: `POST /api/files/upload`, `GET /api/files`, `GET /api/admin/users`
2. Chạy ở 4 mức: 100 / 1.000 / 10.000 / 100.000 user (seed synthentic)
3. Ghi lại: p50/p95/p99, QPS, CPU, RAM, kết nối DB, số query/request (bật EF logging)
4. **Dự đoán bottleneck cần kiểm chứng:**
   - `GET /api/files` không phân trang (đã sửa ở P3-03)
   - Quota denormalized 4 bảng + transaction serializable → lock contention khi nhiều user cùng upload cùng phòng
   - `EnableRetryOnFailure(5×10s)` → storm khi DB chậm
   - Single MySQL instance → cần read replica / cache role-permission ở >10k user
   - Không có cache → mỗi request policy check đọc DB claim (JWT nên OK) nhưng quota thì đọc DB mỗi upload
5. Xuất `review/02_BENCHMARK.md` với số liệu thật + khuyến nghị

### Nghiệm thu
- Có số liệu p95 thực cho từng mức
- Ghi rõ mốc nào hệ thống bắt đầu nghẽn + đề xuất tối ưu cụ thể

---

## ✅ CHECKPOINT SAU PHASE 4

- [ ] `dotnet test` xanh trong CI
- [ ] Gitleaks không phát hiện secret mới
- [ ] `docker compose up` chạy được
- [ ] README đúng sự thật
- [ ] Có file benchmark với số liệu thật
- [ ] Dead code đã dọn, `FormatBytes` chỉ 1 bản
