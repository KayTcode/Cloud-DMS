# SDD Phase 0: Context Discovery — Soft Delete File (Move to Trash)

- **Feature Slug:** `file-soft-delete`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới và quy trình chuyển tệp tin vào thùng rác (`DeleteFile`), duy trì toàn vẹn dữ liệu vật lý trên Cloud, bảo toàn hạn mức Quota và ghi nhận nhật ký kiểm toán trong giao dịch Serializable.
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `DELETE /api/files/{id}` cho phép người dùng chuyển tệp tin đang hoạt động vào Thùng rác (`Recycle Bin`).
2. Cập nhật các trường trạng thái trên bản ghi `FileEntry`:
   - `IsDeleted = true`
   - `DeletedAt = DateTime.UtcNow`
   - `DeletedByUserId = callerUserId`
3. Thực thi nguyên tắc an toàn dữ liệu 2 giai đoạn (Two-Stage Deletion Invariant):
   - **KHÔNG xóa file vật lý trên Cloud Storage** (Google Drive / OneDrive / Local).
   - **CHƯA trừ/hoàn trả dung lượng Quota** của Tenant và User.
4. Bọc toàn bộ thao tác trong Database Transaction với mức cô lập `IsolationLevel.Serializable` để tránh xung đột dữ liệu đồng thời.
5. Ghi bản ghi kiểm toán `AuditLog` với `Action = "MoveToTrash"`.

### WHY (Tại sao cần tính năng này)
- **Bảo vệ người dùng trước việc xóa nhầm:** Cho phép người dùng hoặc quản trị viên có thể khôi phục lại tài liệu khi cần thiết.
- **Tuân thủ quy tắc bảo toàn dữ liệu doanh nghiệp (DATA-01):** Dữ liệu nghiệp vụ cốt lõi không được xóa vật lý ngay lập tức mà phải trải qua giai đoạn lưu giữ tạm thời trong thùng rác.
- **Phòng chống tấn công IDOR:** Ngăn ngừa kẻ tấn công xóa tài liệu của người khác hoặc phòng ban khác.

### Definition of Done (DoD của Pha 0)
- [x] Làm rõ Invariant: File trên Cloud và Quota không thay đổi khi xóa mềm.
- [x] Phân quyền kiểm soát theo vai trò: Employee (chỉ xóa file phòng ban mình), Admin (xóa file phạm vi quản trị).
- [x] Xác định cơ chế khóa giao dịch Serializable.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Xóa nhầm mất vĩnh viễn không thể lấy lại:** Bấm nhầm nút xóa làm mất dữ liệu quan trọng ngay lập tức. | Triển khai cơ chế Xóa mềm (Soft Delete). Tệp tin chỉ ẩn khỏi giao diện chính và chuyển sang Thùng rác, có thể khôi phục nguyên trạng. |
| **Bị kẻ xấu xóa file của phòng ban khác:** Kẻ tấn công gọi API với GUID của file phòng ban khác. | Áp dụng chính sách Fail-Closed: Employee chỉ được xóa file thuộc phòng ban mình; cố tình can thiệp trả về: `"Tệp tin không tồn tại hoặc đã bị xóa."` |
| **Dung lượng bị trừ ảo khi chưa xóa thật:** File vẫn nằm trên Cloud nhưng Quota đã bị trừ dẫn đến lệch số liệu. | Giữ nguyên Quota đến khi file được Xóa vĩnh viễn (`PermanentDelete`). |

---

## 3. Domain Glossary

- **Soft Delete (Xóa mềm):** Thao tác đánh dấu logic bản ghi không còn hoạt động (`IsDeleted = true`) nhưng dữ liệu vật lý vẫn còn nguyên vẹn.
- **Serializable Isolation Transaction:** Mức độ cô lập giao dịch cao nhất trong CSDL, ngăn ngừa triệt để hiện tượng Phantom Reads và Dirty Reads khi cập nhật trạng thái xóa.

---

## 4. System Boundaries & Target Components

- **Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L157-L167) (`DeleteFile`)
- **Command & Handler:** [DeleteFileCommand.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/DeleteFile/DeleteFileCommand.cs) (`DeleteFileCommandHandler`)
- **Entity:** [FileEntry.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Domain/Entities/FileEntry.cs)

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Context làm rõ ràng cơ chế xóa mềm bảo toàn dữ liệu vật lý và phân quyền chống IDOR.
- **Risk:** High (Thao tác thay đổi trạng thái dữ liệu).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
