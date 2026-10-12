# Bảo mật Cloud DMS

## Cơ chế hiện có

- Mật khẩu được băm bằng BCrypt; API xác thực JWT Bearer.
- Policy quyền kiểm tra claim permission; cấu hình nhà cung cấp lưu trữ được mã hóa AES.
- Upload có kiểm tra hạn mức và tích hợp ClamAV.

## Giới hạn hiện tại

- Nhiều endpoint quản trị/tệp chưa gắn Authorize; chưa có policy bảo vệ mặc định toàn API.
- Chưa có bộ lọc tenant toàn cục; upload có tham số override và fallback sang người dùng đang hoạt động.
- ClamAV trả kết quả sạch khi bị tắt hoặc gặp lỗi kết nối/quét.
- CORS trong Program.cs cho phép mọi origin; chưa sử dụng danh sách Cors:AllowedOrigins.
- Program.cs có khóa JWT dự phòng và logic tự xác nhận email người dùng khi khởi động.
- Chưa có refresh/logout hoàn chỉnh; một số controller trả trực tiếp thông báo ngoại lệ.
- Đã có Redis readiness fail-closed và evaluator đọc snapshot quyền nguyên tử; hiện chưa nối vào toàn bộ endpoint/use case nên chưa thay thế cơ chế claim hiện tại.
- Đã có nền coordinator/version/outbox và worker dựng projection; GET trạng thái operation giới hạn theo user khởi tạo. PATCH trạng thái, PUT cập nhật user (bao gồm trạng thái), PUT gán role/chuyển scope user và DELETE user đã qua coordinator, yêu cầu idempotency và trả 202 khi pending. Các luồng dùng global scope nên quyền bị chặn tạm thời trên toàn hệ thống trong khi dựng projection; các mutation khác chưa được chuyển.

## Khi triển khai

Đưa mật khẩu DB/SMTP, khóa JWT/AES và thông tin cloud vào cấu hình bí mật. Rà soát các giới hạn trên, bật HTTPS và loại bỏ dữ liệu/tài khoản mẫu trước khi đưa hệ thống vào sử dụng thực tế.
