# SDD Phase 0: Context Discovery — Recycle Bin Management (Trash Files Listing)

- **Feature Slug:** `file-trash-list`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, ranh giới phân quyền xem thùng rác (`GetTrashFiles`), phân trang, tính tổng dung lượng tệp tin rác chiếm dụng, và cô lập dữ liệu theo vai trò (Employee, DepartmentAdmin, TenantAdmin, SystemAdmin).
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `GET /api/files/trash` cho phép người dùng xem danh sách các tệp tin đang ở trạng thái xóa mềm (`IsDeleted == true`).
2. Thực thi phân quyền hiển thị theo Role:
   - **Nhân viên (`Employee`):** Chỉ xem các tệp tin trong thùng rác do chính mình sở hữu (`OwnerId`) HOẶC do chính mình thực hiện xóa (`DeletedByUserId`).
   - **Trưởng phòng (`DepartmentAdmin`):** Xem toàn bộ các tệp tin trong thùng rác thuộc phòng ban của mình (`DepartmentId`).
   - **Quản trị viên tổ chức (`TenantAdmin`):** Xem toàn bộ tệp tin trong thùng rác của toàn Tenant, hoặc lọc theo phòng ban chỉ định.
   - **Quản trị viên hệ thống (`SystemAdmin`):** Xem thùng rác theo bất kỳ Tenant nào được chỉ định qua `tenantId`.
3. Cung cấp các thông số phân trang (`PageNumber`, `PageSize`), tìm kiếm theo tên tệp tin, và tính toán tổng số lượng (`totalCount`) cùng tổng dung lượng tệp tin rác (`totalSizeBytes`).

### WHY (Tại sao cần tính năng này)
- **Minh bạch hóa dữ liệu chờ xóa:** Giúp người dùng biết những tệp tin nào đang nằm trong thùng rác để ra quyết định khôi phục hoặc xóa hẳn.
- **Kiểm soát dung lượng chiếm dụng:** Thùng rác vẫn chiếm dung lượng Quota của tổ chức; biết được `totalSizeBytes` giúp quản trị viên đánh giá dung lượng có thể thu hồi khi dọn sạch thùng rác.
- **Bảo mật quyền riêng tư cá nhân:** Không cho phép nhân viên này xem thùng rác của nhân viên khác trong cùng tổ chức.

### Definition of Done (DoD của Pha 0)
- [x] Xác định các trường thông tin trả về của `TrashFileItemDto` (gồm người xóa `DeletedByUser`, thời điểm xóa `DeletedAt`).
- [x] Quy định thuật toán lọc theo phân cấp quyền hạn (Employee vs DeptAdmin vs TenantAdmin).
- [x] Đảm bảo hiệu năng truy vấn: Sử dụng `AsNoTracking()` và phân trang an toàn (`Math.Clamp(pageSize, 1, 100)`).

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Nhân viên thấy tệp tin rác của người khác:** Xem trộm các tài liệu người khác đã xóa. | Lọc nghiêm ngặt: Employee chỉ thấy file do mình sở hữu hoặc do mình thực hiện xóa. |
| **Không biết ai đã xóa tệp tin:** Một tài liệu chung phòng ban bị xóa nhưng không rõ người thao tác. | Trả về thông tin chi tiết: `DeletedAt`, `DeletedByUserId`, và họ tên đầy đủ của người xóa `DeletedByUser`. |
| **Không biết thùng rác đang chiếm bao nhiêu dung lượng:** | Tính tổng dung lượng `totalSizeBytes` bằng câu lệnh `SumAsync` trong cùng phạm vi lọc. |

---

## 3. Domain Glossary

- **TrashFileListDto:** DTO kết quả chứa danh sách `TrashFileItemDto`, tổng số lượng bản ghi `TotalCount`, trang hiện tại `PageNumber`, kích thước trang `PageSize`, và tổng dung lượng rác `TotalSizeBytes`.
- **Trash Retention Scope:** Phạm vi các bản ghi đã xóa mềm (`IsDeleted == true`) được lưu giữ tạm thời chờ khôi phục hoặc xóa vĩnh viễn.

---

## 4. System Boundaries & Target Components

- **Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L170-L193) (`GetTrashFiles`)
- **Query & Handler:** [GetTrashFilesQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetTrashFiles/GetTrashFilesQuery.cs) (`GetTrashFilesQueryHandler`)
- **DTOs:** [TrashFileItemDto.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/DTOs/TrashFileItemDto.cs)

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân tích bối cảnh quản lý danh sách thùng rác đã bao quát phân quyền xem theo role và tính toán dung lượng rác.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
