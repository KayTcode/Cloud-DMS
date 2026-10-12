# Báo cáo kết quả kiểm thử

**Ngày kiểm thử:** 12/10/2026  
**Nhánh:** `main`  
**Phạm vi:** Authentication, Authorization và bảo vệ upload file

## Tóm tắt

| Hạng mục | Kết quả |
|---|---:|
| Build solution chính | Đạt |
| Unit tests `CloudDms.UnitTests` | 22/22 đạt |
| Test case authorization riêng lẻ | 1/1 đạt |
| `CleanArchCqrs.Tests` | Chưa chạy được do lỗi biên dịch |
| Integration tests MySQL/Redis | Chưa chạy được vì Docker Engine chưa bật |

## Test case đã chạy thành công

**Tên:** `TenantAdminCannotManageUserInAnotherTenant`  
**Vị trí:** `tests/CloudDms.UnitTests/Authorization/AccessPolicyEvaluatorTests.cs`

**Mục tiêu:** Xác nhận Tenant Admin không thể quản lý người dùng thuộc tenant khác.

**Kết quả mong đợi:** Request bị từ chối.

**Kết quả thực tế:** Request bị từ chối đúng như mong đợi.

**Kết quả:** `Passed`

Output:

```text
Passed CloudDms.UnitTests.Authorization.AccessPolicyEvaluatorTests.TenantAdminCannotManageUserInAnotherTenant [7 ms]

Test Run Successful.
Total tests: 1
     Passed: 1
```

## Nhóm unit test

Lệnh chạy:

```powershell
dotnet test tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj `
  --no-build `
  --logger "console;verbosity=minimal"
```

Kết quả:

```text
Passed: 22
Failed: 0
Skipped: 0
Total: 22
```

## Test upload file đang mở

File `tests/CleanArchCqrs.Tests/Files/FileUploadSecurityTests.cs` gồm các kịch bản:

- System Admin upload file phải bị từ chối.
- User chưa được cấp quota phải bị từ chối.
- File vượt quota cá nhân phải bị từ chối.

Project này hiện chưa chạy được do lỗi namespace trong test khác:

```text
error CS0234:
The type or namespace name 'RefreshTokenAuth' does not exist
in 'CleanArchCqrs.Application.Users.Commands'

File:
tests/CleanArchCqrs.Tests/Auth/RefreshTokenTests.cs
```

Lỗi này xảy ra ở bước compile nên các test upload chưa được thực thi.

## Integration tests

Lệnh chạy:

```powershell
dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj
```

Kết quả hiện tại: chưa thực thi được vì Docker Engine chưa chạy.

Lỗi chính:

```text
failed to connect to the docker API at
npipe:////./pipe/dockerDesktopLinuxEngine
```

Cần mở Docker Desktop rồi khởi động các service:

```powershell
docker compose up -d mysql redis clamav
```

Sau đó chạy lại integration tests.

## Cảnh báo build

Build thành công nhưng còn các cảnh báo kỹ thuật:

- Duplicate package `Microsoft.Extensions.Hosting.Abstractions` phiên bản `8.0.0` và `8.0.1`.
- `GoogleCredential.FromStream` đang bị đánh dấu obsolete.

Các cảnh báo này không làm build thất bại.

## Kết luận

Các unit test chính và test authorization mẫu đang đạt. Bộ test upload trong `CleanArchCqrs.Tests` cần sửa namespace `RefreshTokenAuth` trước khi có thể chạy. Integration tests cần Docker Desktop với MySQL và Redis đang hoạt động.
