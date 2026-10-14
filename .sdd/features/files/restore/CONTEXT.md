# SDD Phase 0: Context Discovery — Restore File from Trash

- **Feature Slug:** `file-restore`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới và quy trình khôi phục tệp tin từ thùng rác (`RestoreFile`), trả tệp tin về danh sách hoạt động chính (`IsDeleted = false`), xóa vết thông tin xóa mềm và ghi nhận nhật ký kiểm toán.
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `POST /api/files/{id}/restore` cho phép người dùng khôi phục tệp tin từ thùng rác trở lại danh sách hoạt động.
2. Cập nhật các trường dữ liệu trên `FileEntry`:
   - `IsDeleted = false`
   - `DeletedAt = null`
   - `DeletedByUserId = null`
3. Phân quyền chặt chẽ theo Role:
   - **Nhân viên (`Employee`):** Chỉ khôi phục tệp tin do mình sở hữu (`OwnerId`) HOẶC thuộc phòng ban của mình.
   - **Trưởng phòng (`DepartmentAdmin`):** Chỉ khôi phục tệp tin thuộc phạm vi phòng ban mình quản lý.
   - **Quản trị viên tổ chức (`TenantAdmin`):** Khôi phục bất kỳ tệp tin nào thuộc tổ chức của mình.
   - **Quản trị viên hệ thống (`SystemAdmin`):** Toàn quyền khôi phục tệp tin.
4. Ghi nhận bản ghi kiểm toán `AuditLog` với `Action = "RestoreFromTrash"`.

### WHY (Tại sao cần tính năng này)
- **Hoàn tác thao tác xóa:** Cho phép khắc phục ngay lập tức các sự cố xóa nhầm tài liệu quan trọng mà không cần can thiệp sao lưu (backup) phức tạp.
- **Tính toàn vẹn dữ liệu:** Vì tệp tin trên Cloud Storage và hạn mức Quota không bị thay đổi khi xóa mềm, việc khôi phục diễn ra tức thì, không cần tải lại file hoặc kiểm tra lại Quota.
- **Phòng chống tấn công IDOR:** Ngăn chặn kẻ tấn công khôi phục file của phòng ban khác hoặc tổ chức khác.

### Definition of Done (DoD của Pha 0)
- [x] Làm rõ Invariant: File khôi phục phải đang ở trạng thái `IsDeleted == true`.
- [x] Phân quyền kiểm soát theo vai trò của người gọi.
- [x] Ghi nhận lịch sử khôi phục vào `AuditLog`.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Xóa nhầm không thể lấy lại:** Người dùng bấm nhầm nút xóa trong lúc làm việc. | Khôi phục lại tài liệu ngay từ Thùng rác chỉ với một nút bấm ("Khôi phục"). Tài liệu xuất hiện lại ở đúng thư mục ban đầu. |
| **Kẻ xấu khôi phục trộm tài liệu của phòng ban khác:** | Kiểm tra chặt chẽ `TenantId` và `DepartmentId`. Nếu không có quyền, từ chối với thông báo: `"Bạn không có quyền khôi phục tệp tin này."` |
| **Khôi phục file không nằm trong thùng rác:** Gọi API restore trên một file đang hoạt động bình thường. | Kiểm tra điều kiện `f.IsDeleted == true`. Nếu file đang hoạt động, trả về: `"Tệp tin không tồn tại trong thùng rác hoặc đã được khôi phục."` |

---

## 3. Domain Glossary

- **Restore Operation:** Thao tác đưa thực thể từ trạng thái thùng rác (`IsDeleted = true`) trở về trạng thái hoạt động bình thường (`IsDeleted = false`).
- **AuditLog Action "RestoreFromTrash":** Bản ghi ghi nhận thời điểm, người thực hiện và đối tượng tệp tin được phục hồi.

---

## 4. System Boundaries & Target Components

- **Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L195-L205) (`RestoreFile`)
- **Command & Handler:** [RestoreFileCommand.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/RestoreFile/RestoreFileCommand.cs) (`RestoreFileCommandHandler`)
- **Entity:** [FileEntry.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Domain/Entities/FileEntry.cs)

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân tích bối cảnh khôi phục tệp tin làm rõ quy tắc nghiệp vụ và cơ chế bảo mật IDOR.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
