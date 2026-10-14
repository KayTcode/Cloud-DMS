# SDD Phase 0: Context Discovery — File Download Subsystem

- **Feature Slug:** `file-download`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới và phân quyền cho tính năng tải xuống tệp tin (`DownloadFile`), giải mã kết nối Cloud Storage Provider (Google Drive, OneDrive, Local), streaming dữ liệu nhị phân về trình duyệt và bảo vệ quyền sở hữu dữ liệu (Anti-IDOR / BOLA).
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cho phép người dùng hợp lệ tải xuống tệp tin (`GET /api/files/{id}/download`) dưới dạng stream nhị phân trực tiếp.
2. Thiết lập header HTTP chuẩn xác (`Content-Type`, `Content-Disposition`, `FileName`) để trình duyệt hiển thị hoặc lưu tệp tin đúng định dạng.
3. Kiểm tra nghiêm ngặt quyền sở hữu: Ngăn chặn tuyệt đối việc người dùng của Tenant A tải file của Tenant B, hoặc nhân viên phòng ban này tải tài liệu phòng ban khác khi chưa được cấp quyền (Anti-IDOR).
4. Tương tác với `IStorageServiceFactory` để lấy stream từ đúng Cloud Storage Provider (Google Drive API, OneDrive Graph API, Local FileSystem) bằng thông tin cấu hình được giải mã trong bộ nhớ.

### WHY (Tại sao cần tính năng này)
- **Truy cập tài nguyên minh bạch:** Người dùng cần tải tài liệu về máy trạm để phục vụ công việc mà không cần biết file đang lưu trữ thực tế ở Google Drive, OneDrive hay Local Server.
- **Bảo mật dữ liệu tuyệt đối (Anti-Data Exfiltration):** Ngăn ngừa rò rỉ dữ liệu qua việc đoán GUID tệp tin (IDOR).
- **Tối ưu tài nguyên máy chủ:** Truyền dữ liệu dạng Stream (`StreamContent`), không nạp toàn bộ file lớn vào bộ nhớ RAM của Web Server.

### Definition of Done (DoD của Pha 0)
- [x] Xác định các tác nhân, quyền hạn và chính sách bảo mật endpoint (`file.read`).
- [x] Xác định luồng dữ liệu nhị phân (Streaming Data Flow) từ Cloud Storage về Client.
- [x] Lập danh mục rủi ro: Lỗ hổng IDOR, rò rỉ stream, nghẽn bộ nhớ khi tải file lớn.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Nguy cơ IDOR đoán ID tệp tin:** Kẻ xấu thay đổi `FileId` trên URL để tải trộm tài liệu mật của phòng ban khác hoặc doanh nghiệp khác. | Phân quyền 3 tầng trong Use Case: SystemAdmin có quyền toàn hệ thống; TenantAdmin bị giới hạn trong Tenant; Employee chỉ được tải tài liệu thuộc phòng ban của mình. Tệp tin không hợp lệ trả về Fail-Closed: `"Tệp tin không tồn tại hoặc đã bị xóa."` |
| **Treo RAM máy chủ khi tải file lớn:** Tải nhiều file 50-100MB đồng thời có thể gây tràn RAM nếu đọc toàn bộ `byte[]`. | Sử dụng `Stream` đọc tuần tự từ Cloud Provider chuyển tiếp thẳng vào `File(Stream, ContentType, FileName)` của ASP.NET Core ActionResult. |
| **Tệp tin đã bị xóa mềm vẫn tải được:** File đã chuyển vào thùng rác nhưng người dùng vẫn truy cập được bằng URL tải trực tiếp. | Truy vấn bắt buộc lọc điều kiện `!f.IsDeleted`. Tệp trong thùng rác bị chặn tải xuống cho đến khi được khôi phục. |

---

## 3. Domain Glossary

- **DownloadFileResult:** Đối tượng chứa luồng nhị phân `Stream`, tên tệp tin `FileName`, và định dạng `ContentType` trả về từ Use Case.
- **Streaming Transfer:** Cơ chế chuyển tiếp luồng nhị phân trực tiếp từ Cloud Provider tới HTTP Response Stream mà không lưu đệm toàn bộ file ra đĩa máy chủ hoặc RAM.
- **Fail-Closed Security Response:** Trả về cùng một thông báo lỗi chung khi file không tồn tại hoặc caller không có quyền, nhằm ngăn chặn việc dò quét sự tồn tại của tệp tin (Information Disclosure).

---

## 4. System Boundaries & Target Components

- **Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L141-L154) (`DownloadFile`)
- **Query & Handler:** [DownloadFileQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/DownloadFile/DownloadFileQuery.cs) (`DownloadFileQueryHandler`)
- **Storage Services:** [LocalStorageService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Infrastructure/Services/Storage/LocalStorageService.cs#L59), [GoogleDriveStorageService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Infrastructure/Services/Storage/GoogleDriveStorageService.cs#L98)

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân tích bối cảnh tính năng tải xuống tệp tin đã bao quát các rủi ro bảo mật IDOR và hiệu năng stream dữ liệu.
- **Risk:** High (Truy cập dữ liệu nhị phân nhạy cảm).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
