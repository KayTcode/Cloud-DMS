# Kiến trúc Cloud DMS

## Tổng quan

Hệ thống hiện là một ứng dụng API triển khai chung, gồm bốn project .NET 8.

| Tầng | Trách nhiệm |
|---|---|
| CleanArchCqrs.API | Controller, JWT Bearer, policy quyền, Swagger và đăng ký dịch vụ. |
| CleanArchCqrs.Application | Command/query, handler, DTO, validator và interface dịch vụ. |
| CleanArchCqrs.Domain | Entity nghiệp vụ, BaseEntity và ngoại lệ miền. |
| CleanArchCqrs.Infrastructure | EF Core/MySQL, repository, JWT, BCrypt, SMTP, AES, lưu trữ và ClamAV. |

Phụ thuộc project: API → Application + Infrastructure; Infrastructure → Application + Domain; Application → Domain. Domain không tham chiếu các tầng còn lại.

## Luồng xử lý

```text
Client → Controller → MediatR → ValidationBehavior → Handler
                                                     ↓
                                   Interface trong Application
                                                     ↓
                          Infrastructure → MySQL / kho tệp / SMTP
```

API đăng ký ValidationBehavior; LoggingBehavior có trong mã nguồn nhưng chưa được đăng ký. GlobalExceptionHandlerMiddleware cũng chưa được gắn vào pipeline.

## Dữ liệu và lưu trữ

- Nhóm tổ chức: Tenant, Department, User.
- Nhóm quyền: Role, Permission, UserRole, RolePermission.
- Nhóm tài liệu: FileEntry, Folder, FileShare; metadata lưu trong MySQL.
- Nhóm hỗ trợ: StorageProvider, UserStorageQuota, EmailVerificationToken, RefreshToken, AuditLog; còn entity Product từ mẫu ban đầu.
- StorageServiceFactory chọn Local, Google Drive hoặc OneDrive; cấu hình provider được mã hóa AES.
- Upload kiểm tra hạn mức tenant/người dùng/provider, gọi ClamAV, lưu tệp rồi cập nhật metadata, dung lượng và nhật ký.

## Phạm vi hiện tại

Xác thực nằm trong API, quyền được nhúng vào JWT. Chưa có Auth Service riêng, cấu hình Kong, Redis hay Docker Compose. AppDbContext chưa có bộ lọc tenant toàn cục; một số handler nhận tenant từ request hoặc có cơ chế fallback danh tính, nên chưa thể khẳng định cách ly tenant hoàn chỉnh.
