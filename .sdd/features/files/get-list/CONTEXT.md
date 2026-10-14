# SDD Phase 0: Context Discovery — File Listing & Storage Quota Summary

- **Feature Slug:** `file-get-list`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Scope:** Khảo sát bối cảnh, phân quyền hiển thị danh sách tài liệu đang hoạt động (`GetTenantFiles`), lọc theo phòng ban và thư mục, tìm kiếm theo tên, và tính toán bảng tổng hợp hạn mức lưu trữ (Quota Summary) cho tổ chức hoặc phòng ban.
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

### WHAT (Hệ thống cần làm gì ở góc độ người dùng & nghiệp vụ)
1. Cung cấp API `GET /api/files` cho phép người dùng xem danh sách tệp tin đang hoạt động (`!IsDeleted`) theo phân quyền.
2. Hỗ trợ lọc theo phòng ban (`departmentId`), thư mục (`folderId`) và tìm kiếm theo từ khóa (`searchTerm`).
3. Tự động thu hẹp phạm vi dữ liệu (Scope Enforcing):
   - Nhân viên (`Employee`) và Trưởng phòng (`DepartmentAdmin`) chỉ xem được danh sách tệp tin thuộc phòng ban của mình.
   - Quản trị viên tổ chức (`TenantAdmin`) có thể xem toàn bộ tệp tin trong Tenant hoặc lọc theo từng phòng ban.
4. Trả về bảng tổng quan hạn ngạch lưu trữ (`TenantStorageSummaryDto`): Dung lượng cấp phát (`quota`), dung lượng đã dùng (`used`), dung lượng còn trống (`available`), và tỷ lệ tiêu thụ phần trăm (`usagePercent`).

### WHY (Tại sao cần tính năng này)
- **Minh bạch hóa tài nguyên:** Người dùng và quản trị viên cần nắm rõ hạn mức lưu trữ của tổ chức/phòng ban để có kế hoạch dọn dẹp hoặc mở rộng dung lượng kịp thời.
- **Cô lập dữ liệu nghiêm ngặt:** Đảm bảo không người dùng nào xem được danh sách tệp tin của Tenant khác hoặc phòng ban khác khi chưa được phân quyền.
- **Hỗ trợ điều hướng thư mục:** Cung cấp dữ liệu tệp tin theo từng thư mục phục vụ giao diện cây tài liệu (File Explorer).

### Definition of Done (DoD của Pha 0)
- [x] Xác định rõ ràng các tham số truy vấn (`tenantId`, `departmentId`, `folderId`, `pageNumber`, `pageSize`, `searchTerm`).
- [x] Phân định ranh giới tính toán Quota phân cấp giữa Tenant và Department.
- [x] Đảm bảo truy vấn tối ưu, không đọc trường không cần thiết và loại bỏ file trong thùng rác.

---

## 2. Business Context & User Pain Points

| Hiện trạng & Vấn đề (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Nhân viên xem trộm tài liệu phòng ban khác:** Không phân quyền ranh giới phòng ban dẫn đến rò rỉ danh sách tài liệu nhạy cảm. | Tự động gán `targetDepartmentId` theo phòng ban của nhân viên nếu role là `Employee` hoặc `DepartmentAdmin`. |
| **Không biết còn bao nhiêu dung lượng trống:** Người dùng tải file lên mới biết bị lỗi hết dung lượng. | Trả về thông tin Quota tổng hợp ngay trong phản hồi danh sách tệp tin (`quota`, `used`, `available`, `usagePercent`) để FE hiển thị thanh dung lượng trực quan. |
| **Tải danh sách chậm khi số lượng file lớn:** Lấy toàn bộ bản ghi mà không phân trang gây quá tải đường truyền. | Hỗ trợ phân trang và tìm kiếm theo tên tệp tin không phân biệt hoa thường. |

---

## 3. Domain Glossary

- **TenantStorageSummaryDto:** DTO tổng hợp chứa thông tin Tenant/Phòng ban, các chỉ số Quota (`StorageQuotaBytes`, `StorageUsedBytes`, `AvailableBytes`, `UsagePercent`), thông tin Storage Provider và danh sách các tệp tin (`Files`).
- **Department Scope Enforcement:** Cơ chế tự động khóa phạm vi truy vấn về phòng ban của người dùng nếu tài khoản không có quyền quản trị toàn tổ chức.

---

## 4. System Boundaries & Target Components

- **Controller:** [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs#L110-L136) (`GetTenantFiles`)
- **Query & Handler:** [GetTenantFilesQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQuery.cs), [GetTenantFilesQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs)
- **DTOs:** [TenantStorageSummaryDto.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/Files/DTOs/FileEntryDto.cs)

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Bối cảnh và ranh giới phân quyền cho tính năng xem danh sách tệp tin và tổng hợp hạn mức lưu trữ đã được xác lập đầy đủ.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
