# SDD Phase 0: Context Discovery — Setup Department (Tự thiết lập Phòng ban)

- **Module:** `DepartmentAdmin`
- **Feature Slug:** `setup-department`
- **Version:** `1.5.0`
- **Scope:** Khởi tạo thông tin phòng ban, chuyển đổi hạn mức từ ví tạm sang phòng ban chính thức, trích quota cá nhân Trưởng phòng, tự động sinh thư mục gốc, truy xuất hạn mức khởi tạo ban đầu, nhận diện phòng ban đã hoàn tất thiết lập và cơ chế bảo toàn phân quyền tức thì cho Dashboard.
- **Status:** `APPROVED`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  - Cung cấp tính năng "Self-Setup" cho Trưởng phòng (`DepartmentAdmin`) sau khi kích hoạt tài khoản thành công qua thư mời Email.
  - Cho phép Trưởng phòng tự đặt Tên phòng ban (`Name`), Mã phòng ban (`Code`), Mô tả (`Description`), và lựa chọn số GB hạn mức cá nhân giữ lại (`personalQuota`), phần còn lại chuyển thành ngân sách chung của phòng ban để phân bổ cho nhân viên.
  - Cung cấp API truy xuất thông tin hạn mức ví tạm ban đầu (`GET /api/department/setup-info`) đọc trực tiếp từ ví tạm `UserStorageQuota` do TenantAdmin đã phân bổ theo EARS-03. Giao diện máy khách hiển thị chính xác trần dung lượng được cấp, loại bỏ hoàn toàn việc hardcode dung lượng đĩa.
  - Tự động liên kết tài khoản Trưởng phòng vào phòng ban mới và tự động khởi tạo Thư mục gốc (`Folder`) làm không gian lưu trữ chung.
- **WHY (Tại sao cần tính năng này):**
  - Hiện thực hóa mô hình "Admin-First / Self-Setup": Quản trị viên Tổ chức (`TenantAdmin`) không cần phải tạo sẵn phòng ban hay cấu trúc cây thư mục; TenantAdmin giao ngân sách dung lượng ("ví Quota") cho Trưởng phòng qua `UserStorageQuota` (tự động trích từ dung lượng khả dụng của Tenant theo EARS-03), Trưởng phòng chủ động định hình cơ cấu phòng ban của mình.
  - Bảo đảm phân quyền chuẩn xác (RBAC): Sử dụng policy `department.manage` phù hợp với vai trò của `DepartmentAdmin` thay vì `department.create` (vốn chỉ dành cho cấp TenantAdmin/SystemAdmin), ngăn chặn lỗi từ chối quyền `403 Forbidden`.
  - Bảo toàn dung lượng lưu trữ: Toàn bộ quota từ ví tạm `UserStorageQuota` được chuyển sang `Department.StorageQuotaBytes` chính xác, không thất thoát.
  - Bảo đảm tính toàn vẹn cơ sở dữ liệu: Tránh lỗi trùng lặp mã phòng ban trong cùng Tenant và chống xung đột Unique Index khi khởi tạo thư mục gốc.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ràng phạm vi phân quyền RBAC và endpoint tương ứng (`department.manage`).
  - Quy định đầy đủ luồng dữ liệu 2 bước: `GET /api/department/setup-info` (lấy hạn mức ví tạm) -> `POST /api/department/setup` (thiết lập).
  - Thu thập thuật ngữ nghiệp vụ và quy tắc toàn vẹn thư mục gốc.
- **Decision Owner:** Product Owner & Tech Lead (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Giao diện hiển thị hardcode dung lượng trong khi hạn mức thực tế của Trưởng phòng khác biệt | Cung cấp endpoint `GET /api/department/setup-info` đọc trực tiếp dung lượng ví tạm từ `UserStorageQuota`; giao diện tự động điều chỉnh trần thanh trượt slider (`max`) theo số thực tế. |
| Trưởng phòng bấm "Hoàn tất & Kích hoạt" bị lỗi từ chối `403 Forbidden` do yêu cầu policy `department.create` | Chuyển chính sách cấp quyền sang `department.manage` (khớp đúng quyền hạn của `DepartmentAdmin` trong hệ thống RBAC). |
| Lỗi CSDL vi phạm Unique Index `(TenantId, ParentFolderId, Name)` khi tạo thư mục gốc trùng tên | Kiểm tra sự tồn tại của thư mục gốc trước khi tạo; nếu đã có thì liên kết thay vì tạo mới. |
| Trưởng phòng đã thiết lập phòng ban rồi cố tạo thêm phòng ban thứ hai | Kiểm tra nghiêm ngặt `user.DepartmentId.HasValue`, từ chối ngay lập tức để duy trì nguyên tắc mỗi Trưởng phòng chỉ quản lý 1 phòng ban. |
| Giao diện hiển thị thông báo lỗi chung chung không rõ nguyên nhân | Trả về cấu trúc `Result.Error` chuẩn hóa và giao diện bóc tách chi tiết thông điệp lỗi (`err.message`) để người dùng biết cách khắc phục. |
| Giá trị mặc định hardcode (10 GB / 5 GB) che giấu trạng thái thực của Quota | Loại bỏ hoàn toàn giá trị hardcode; hệ thống đọc hạn mức thực từ `UserStorageQuota`, nếu tài khoản chưa được cấp Quota thì trả lỗi rõ ràng. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **Self-Setup:** Quy trình Trưởng phòng tự khởi tạo thực thể phòng ban từ số Quota ban đầu được TenantAdmin phân bổ vào ví tạm `UserStorageQuota`.
- **Ví Quota tạm (`UserStorageQuota` trước khi Setup):** Bản ghi hạn mức lưu trữ cá nhân được gán cho tài khoản Trưởng phòng khi được TenantAdmin mời (chưa gắn `DepartmentId`), được cấp trích từ dung lượng khả dụng của Tenant theo EARS-03.
- **Admin Personal Quota:** Số dung lượng (GB hoặc Bytes) mà Trưởng phòng quyết định giữ lại cho tài khoản cá nhân của mình khi khởi tạo phòng ban.
- **Department Storage Quota:** Tổng ngân sách dung lượng của phòng ban sau khi setup, bằng tổng số Quota ví tạm được cấp.
- **Ngân sách còn lại cho nhân viên (`RemainingForEmployees`):** Bằng `Department.StorageQuotaBytes - personalQuota`.
- **Department Root Folder:** Thư mục cấp cao nhất của phòng ban (`ParentFolderId = null`, `DepartmentId = department.Id`).
- **Policy `department.manage`:** Quyền quản lý nghiệp vụ phòng ban được cấp cho Trưởng phòng trong hệ thống RBAC.

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi)

### Trong phạm vi (In-Scope)
- Truy xuất thông tin hạn mức ví tạm qua `GetDepartmentSetupInfoQuery` (`GET /api/department/setup-info`).
- Xác thực dữ liệu đầu vào bằng FluentValidation (`SetupDepartmentCommandValidator`): Tên (2-100), Mã Regex `^[a-zA-Z0-9_-]{2,50}$`, Quota cá nhân `>= 0`.
- Chuyển Quota ví tạm thành Quota phòng ban chính thức (`SetupDepartmentCommandHandler`).
- Gắn tài khoản Trưởng phòng vào phòng ban mới (`user.DepartmentId = department.Id`).
- Khởi tạo hoặc liên kết Thư mục gốc an toàn (`Folder`).
- Ghi lịch sử kiểm toán `AuditLog` với `Action = "SetupDepartment"`.
- Giao diện người dùng động: Tính toán trực quan số GB cá nhân giữ lại và số GB còn lại cho nhân viên.

### Loại trừ (Out-of-Scope)
- Tạo phòng ban thứ 2 cho cùng một Trưởng phòng.
- TenantAdmin tạo phòng ban thay Trưởng phòng (luồng đó thuộc về `TenantAdmin` module tại `/api/tenant/{id}/departments`).
- Phân bổ Quota cho từng nhân viên cụ thể (thực hiện ở tính năng `allocate-user-storage-quota` sau khi đã có nhân viên).

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Nghiệp vụ chuyển đổi tài nguyên dung lượng và tạo lập không gian lưu trữ cốt lõi; yêu cầu phân quyền chuẩn xác, bảo đảm tính toàn vẹn CSDL và hiển thị hạn mức chính xác 100%.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Policy nào là chuẩn xác cho Trưởng phòng tự thiết lập phòng ban? | Dùng `department.manage` vì Trưởng phòng đã sở hữu quyền này trong token, không dùng `department.create`. | `RESOLVED` |
| 2 | Giao diện lấy trần dung lượng tối đa từ đâu để tránh hardcode? | Gọi `GET /api/department/setup-info` lấy `assignedQuotaGB` từ CSDL ví tạm của Trưởng phòng. | `RESOLVED` |
| 3 | Xử lý thế nào nếu thư mục gốc cùng tên phòng ban đã tồn tại ở cấp root? | Tái sử dụng thư mục gốc sẵn có và cập nhật `DepartmentId`, không tạo bản ghi mới để tránh lỗi unique constraint. | `RESOLVED` |
| 4 | Trưởng phòng có thể giữ lại toàn bộ quota phòng ban cho mình không? | Có thể (nếu phòng chưa có nhân viên), nhưng không được vượt quá tổng quota được cấp. Khuyến nghị giữ lại 2-5 GB. | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Tính năng `setup-department` hoàn thiện luồng Self-Setup cho Trưởng phòng: Gồm query lấy thông tin ví quota (`setup-info`) và lệnh thiết lập phòng ban (`setup`). Sử dụng policy `department.manage`, cấu trúc tách biệt Command/CommandHandler/Validator và xử lý CSDL an toàn 100%.
- **Đối chiếu mã nguồn:**
  - Backend Controller: [DepartmentSetupController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/DepartmentSetupController.cs)
  - Backend Command: [SetupDepartmentCommand.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Commands/SetupDepartment/SetupDepartmentCommand.cs) & [SetupDepartmentCommandHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Commands/SetupDepartment/SetupDepartmentCommandHandler.cs)
  - Backend Validator: [SetupDepartmentCommandValidator.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Commands/SetupDepartment/SetupDepartmentCommandValidator.cs)
  - Backend Query: [GetDepartmentSetupInfoQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Queries/GetDepartmentSetupInfo/GetDepartmentSetupInfoQuery.cs) & [GetDepartmentSetupInfoQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Queries/GetDepartmentSetupInfo/GetDepartmentSetupInfoQueryHandler.cs)
  - Frontend View: [SetupDepartmentView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/department-admin/views/SetupDepartmentView.tsx)

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Ghi chú:** Bản ngữ cảnh CONTEXT.md đã được cập nhật lên v1.4.0 đồng bộ hoàn toàn với mã nguồn thực tế: Bỏ toàn bộ fallback hardcode/Tenant; đọc trực tiếp từ `UserStorageQuota`; kiểm tra lỗi khi Quota ví tạm <= 0; chuẩn hóa phân quyền policy `department.manage`; tách file Command/Query/Handler riêng biệt theo Clean Architecture.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản ngữ cảnh setup-department v1.4.0.
- **Timestamp:** 2026-10-14T00:30:00+07:00
