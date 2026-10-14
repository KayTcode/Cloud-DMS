# SDD Phase 0: Context Discovery — Permanent Delete File & Quota Reclamation

- **Feature Slug:** `file-permanent-delete`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới và quy trình xóa vĩnh viễn tệp tin (`PermanentDeleteFile`), hủy dữ liệu vật lý trên Cloud Storage Provider, xóa bản ghi CSDL và hoàn trả dung lượng Quota sử dụng trong giao dịch Serializable.
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `DELETE /api/files/{id}/permanent` cho phép người dùng xóa triệt để một tệp tin đang nằm trong Thùng rác (`IsDeleted == true`).
2. Quy trình xử lý 2 bước an toàn (Two-Step Destruction Workflow):
   - **Bước 1 (Xóa file vật lý trên Cloud Storage):** Gọi `storageService.DeleteFileAsync(fileEntry.StorageKey)` ngoài phạm vi Database Transaction để tránh giữ khóa kết nối mạng quá lâu. Nếu xóa Cloud thất bại hoặc tệp tin đã biến mất, vẫn tiếp tục để bảo đảm giải phóng dung lượng cho doanh nghiệp.
   - **Bước 2 (Hoàn trả Quota & Xóa CSDL):** Mở giao dịch CSDL với mức độ cô lập `IsolationLevel.Serializable`:
     - Giảm `UsedCapacityBytes` của `StorageProvider`.
     - Giảm `StorageUsedBytes` của `Tenant`.
     - Giảm `StorageUsedBytes` của `Department`.
     - Giảm `UsedBytes` của `UserStorageQuota` cá nhân (nếu có).
     - Ghi bản ghi kiểm toán `AuditLog` với `Action = "PermanentDelete"`.
     - Xóa vĩnh viễn bản ghi `FileEntry` khỏi bảng `FileEntries`.
3. Phân quyền chặt chẽ:
   - **Nhân viên (`Employee`):** Chỉ được xóa vĩnh viễn tệp tin do chính mình sở hữu (`OwnerId`).
   - **Trưởng phòng (`DepartmentAdmin`):** Chỉ xóa vĩnh viễn tệp tin thuộc phòng ban của mình.
   - **Quản trị viên tổ chức (`TenantAdmin`) / Quản trị viên hệ thống (`SystemAdmin`):** Có quyền xóa vĩnh viễn tệp tin trong phạm vi quản lý.

### WHY (Tại sao cần tính năng này)
- **Giải phóng dung lượng lưu trữ thực tế (Quota Reclamation):** Thùng rác không thể giữ mãi mãi; khi doanh nghiệp cần thu hồi dung lượng trống, xóa vĩnh viễn là thao tác duy nhất giúp hoàn trả Quota cho người dùng, phòng ban và tổ chức.
- **Tuân thủ quyền được quên (Right to be Forgotten) & Quy định bảo vệ dữ liệu:** Đảm bảo dữ liệu nhạy cảm được tiêu hủy hoàn toàn cả trên Cloud lẫn CSDL khi có yêu cầu.
- **Chống lỗi cạn kiệt dung lượng ảo:** Đảm bảo mọi byte được hoàn trả chính xác, không bị âm dung lượng (`Math.Max(0, used - size)`).

### Definition of Done (DoD của Pha 0)
- [x] Làm rõ nguyên tắc tách biệt: Xóa Cloud trước (ngoài transaction), cập nhật CSDL và Quota trong giao dịch `Serializable`.
- [x] Xác định công thức hoàn trả dung lượng phân tầng: StorageProvider, Tenant, Department, User.
- [x] Phân quyền xóa vĩnh viễn nghiêm ngặt chống IDOR.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Xóa file nhưng dung lượng Quota không giảm:** Tệp tin đã xóa nhưng thanh dung lượng vẫn báo đầy. | Hoàn trả chính xác `SizeBytes` cho cả 4 tầng: Provider, Tenant, Department và User Storage Quota trong cùng một transaction. |
| **Treo Database do chờ xóa Cloud API:** Mạng chập chờn khi gọi Google Drive làm block toàn bộ bảng CSDL. | Gọi `storageService.DeleteFileAsync` bên ngoài transaction CSDL trước; chỉ mở Serializable Transaction khi cập nhật bảng CSDL. |
| **Nhân viên tự ý xóa vĩnh viễn tài liệu của đồng nghiệp:** | Nhân viên chỉ được xóa vĩnh viễn file do mình sở hữu (`OwnerId == callerUserId`). Mọi hành vi khác bị chặn ngay lập tức. |

---

## 3. Domain Glossary

- **Permanent Delete (Xóa vĩnh viễn / Hard Delete):** Thao tác tiêu hủy hoàn toàn dữ liệu nhị phân trên Cloud và xóa bản ghi siêu dữ liệu trong CSDL, không thể hoàn tác.
- **Quota Reclamation (Thu hồi hạn mức):** Quá trình trừ bớt dung lượng đã sử dụng (`UsedBytes -= SizeBytes`) để tăng dung lượng khả dụng cho các thao tác tải lên tiếp theo.

---

## 4. System Boundaries & Target Components

- **Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L210-L218) (`PermanentDeleteFile`)
- **Command & Handler:** [PermanentDeleteFileCommand.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Commands/PermanentDeleteFile/PermanentDeleteFileCommand.cs) (`PermanentDeleteFileCommandHandler`)
- **Storage Services:** [LocalStorageService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Infrastructure/Services/Storage/LocalStorageService.cs#L78), [GoogleDriveStorageService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Infrastructure/Services/Storage/GoogleDriveStorageService.cs#L106)

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân tích bối cảnh xóa vĩnh viễn đã bao quát quy trình hoàn trả Quota 4 tầng an toàn và chống khóa chết CSDL.
- **Risk:** High (Thao tác phá hủy dữ liệu vĩnh viễn).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
