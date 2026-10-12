# Hướng dẫn hỗ trợ phát triển

Áp dụng [AGENTS.md](AGENTS.md). Bắt buộc đọc [ARCHITECTURE.md](ARCHITECTURE.md) và [SECURITY.md](SECURITY.md) trước khi thực hiện thay đổi.

- Đọc mã nguồn trước khi kết luận; nêu rõ giả định khi thiếu thông tin.
- Chọn giải pháp đơn giản, chỉ thay đổi phần liên quan đến yêu cầu.
- Giữ quy tắc phụ thuộc Clean Architecture và luồng CQRS qua MediatR.
- Không tự thêm microservice, Gateway hoặc Redis khi chưa được yêu cầu.
- Kiểm tra kết quả trước khi báo hoàn tất; nói rõ phần chưa kiểm chứng.
- Viết tài liệu và phản hồi bằng tiếng Việt, ngắn gọn.
