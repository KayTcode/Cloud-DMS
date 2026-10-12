# Cloud DMS

Hệ thống quản lý tài liệu đa tổ chức (tenant), xây dựng bằng ASP.NET Core 8 theo Clean Architecture và CQRS.

## Công nghệ và chức năng

- MediatR xử lý command/query; FluentValidation kiểm tra đầu vào.
- EF Core và Pomelo kết nối MySQL 8.
- Đăng nhập JWT, phân quyền theo role và permission; gửi lời mời qua email SMTP.
- Quản lý người dùng, phòng ban, nhà cung cấp lưu trữ và hạn mức dung lượng.
- Tải lên, liệt kê, tải xuống và xóa tệp; có bộ kết nối Local, Google Drive, OneDrive và ClamAV.

## Chạy cục bộ

1. Cài .NET SDK 8 và chuẩn bị MySQL 8.
2. Cấu hình API: `ConnectionStrings:DefaultConnection`, `JwtSettings`, `EmailSettings` bằng biến môi trường hoặc User Secrets.
3. Khi cần, bổ sung `Storage:LocalBasePath`, `Encryption:SecretKey` và `ClamAV:Host/Port/Enabled`.
4. Chạy các lệnh sau tại thư mục gốc:

```powershell
dotnet restore
dotnet run --project src/CleanArchCqrs.API --environment Development
```

Swagger nằm tại đường dẫn gốc của API trong môi trường Development. Khi khởi động, API thử chạy migration, bổ sung bảng/cột và seed dữ liệu mẫu.

## Tài liệu

- [Kiến trúc](ARCHITECTURE.md)
- [Xác thực và phân quyền](sso-auth-spec.md)
- [Bảo mật](SECURITY.md)

Hiện chưa có frontend, Gateway, Redis hoặc bộ kiểm thử trong repository; các thư mục Gateway và tests/AuthService.UnitTests đang trống.
