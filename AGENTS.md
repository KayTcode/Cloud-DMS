# Hướng dẫn làm việc với Cloud DMS

- Bắt buộc đọc [ARCHITECTURE.md](ARCHITECTURE.md) và [SECURITY.md](SECURITY.md) trước khi thực hiện thay đổi.
- Giữ cấu trúc bốn tầng: API, Application, Domain, Infrastructure; dự án dùng .NET 8.
- Đặt use case trong command/query và handler của Application; tích hợp bên ngoài trong Infrastructure.
- Interface dịch vụ nằm tại Application/Common/Interfaces; entity nằm tại Domain/Entities.
- Chỉ sửa phạm vi được yêu cầu; không ghi bí mật vào mã nguồn hoặc tài liệu.
- Tài liệu viết bằng tiếng Việt, ngắn gọn; phân biệt phần đã triển khai và phần dự kiến.
- Kiểm tra phù hợp với thay đổi; thư mục tests hiện chưa có bộ kiểm thử.
