# SDD Phase 0: Context Discovery — DepartmentAdmin Features

- **Module:** `DepartmentAdmin`
- **Version:** `1.2.0`
- **Scope:** 4 Core Features (`setup-department`, `allocate-user-storage-quota`, `create-employee`, `get-department-users-storage-quota`) và các luồng quản trị phòng ban liên quan
- **Status:** `APPROVED`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp cổng quản trị cấp phòng ban (Department Administration) cho Trưởng phòng (`DepartmentAdmin`) để tự thiết lập phòng ban của mình (sau khi được TenantAdmin bổ nhiệm qua mô hình Self-Setup), quản lý tuyển dụng/mời nhân viên vào phòng (`Employee`), điều phối và kiểm soát hạn mức lưu trữ cá nhân (`UserStorageQuota`) cho từng nhân viên từ ngân sách quota của phòng ban, và theo dõi tình hình sử dụng tài nguyên đĩa của các thành viên dưới quyền.
- **WHY (Tại sao cần tính năng này):**
  - Thực thi kiến trúc phân cấp quản trị Quota 3 tầng (Tenant → Department → User): TenantAdmin chỉ quản lý đến cấp phòng ban; Trưởng phòng là người hiểu rõ nhất nhu cầu dung lượng của từng nhân viên để phân bổ phù hợp.
  - Hỗ trợ luồng nghiệp vụ "Admin-First / Self-Setup": Trưởng phòng nhận trước "ví quota" từ TenantAdmin, kích hoạt tài khoản qua Email rồi chủ động định nghĩa tên, mã phòng ban, thư mục gốc và quyết định giữ lại bao nhiêu quota cho cá nhân.
  - Ngăn ngừa tình trạng nhân viên tải file tràn lan gây cạn kiệt đĩa chung của phòng ban; đồng thời bảo vệ dữ liệu người dùng không bị mất mát khi điều chỉnh hạn mức.
  - Đảm bảo ranh giới bảo mật nghiêm ngặt: Trưởng phòng chỉ có đặc quyền trong phạm vi phòng ban của mình (Department-level Isolation), không được phép can thiệp nhân sự hoặc quota của phòng ban khác.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ranh giới nghiệp vụ (Boundaries & Exclusions) và ranh giới cô lập phòng ban (Department Isolation) lẫn tổ chức (Tenant Isolation).
  - Thu thập đầy đủ thuật ngữ nghiệp vụ (Domain Glossary) về hạn mức cá nhân, cấu trúc thư mục gốc và nguyên tắc Fail-Closed Authorization.
  - Phân loại và gán disposition cho các giả định/câu hỏi mở.
- **Decision Owner:** Product Owner & Tech Lead (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Trưởng phòng sau khi nhận lời mời chưa có phòng ban để làm việc ngay | Áp dụng mô hình Self-Setup: Chuyển toàn bộ quota trong "ví tạm" thành quota của phòng ban mới, tự động tạo thư mục gốc và cho phép Trưởng phòng tự chọn quota cá nhân. |
| Nguy cơ hạ quota của nhân viên thấp hơn dữ liệu họ đã upload gây hỏng hoặc khóa file oan | Áp dụng cơ chế kiểm tra an toàn: Không cho phép hạ quota xuống thấp hơn dung lượng thực tế nhân viên đã sử dụng (`UsedBytes`). |
| Cấp quota cho nhân viên vượt quá trần dung lượng mà TenantAdmin đã giao cho phòng ban | Kiểm tra ràng buộc: Tổng quota cấp cho các nhân viên khác + quota mới phải luôn `<= Department.StorageQuotaBytes`. |
| Tranh chấp dữ liệu (Race Condition) khi Trưởng phòng cấp quota đồng thời cho nhiều nhân viên | Đóng gói toàn bộ logic điều phối quota nhân viên trong Database Transaction với mức cô lập `IsolationLevel.Serializable`. |
| Trưởng phòng hoặc người dùng xem/sửa quota hoặc danh sách nhân viên phòng ban khác (BOLA/IDOR) | Áp dụng nguyên tắc **Fail-Closed**: Bắt buộc cùng `TenantId` (cho mọi role trừ SystemAdmin) và bắt buộc đúng `DepartmentId` (nếu không phải TenantAdmin/SystemAdmin); từ chối ngay lập tức khi `DepartmentId` là `null`. |
| Lỗi sập hệ thống HTTP 500 khi client gửi payload rỗng hoặc cấp quota âm | Tiền kiểm soát dữ liệu tại ranh giới ứng dụng bằng **FluentValidation** (`StorageQuotaBytes >= 0`, Email hợp lệ, Regex mã phòng ban). |
| Giá trị hardcode hoặc fallback Tenant che giấu lỗi Quota ví tạm của Trưởng phòng | Đọc trực tiếp Quota từ ví tạm `UserStorageQuota`; nếu `<= 0` hoặc chưa có thì từ chối ngay với lỗi rõ ràng yêu cầu liên hệ TenantAdmin; không fallback ngầm. |
| Rò rỉ thông tin chi tiết CSDL/Exception ra ngoài client | Thực thi **Safe Error Contract (ENG-02)**: Ghi log chi tiết tại máy chủ qua `ILogger` và trả về thông báo lỗi thân thiện, chuẩn hóa cho người dùng. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **DepartmentAdmin:** Trưởng phòng ban, chịu trách nhiệm quản lý nhân sự, hạn mức lưu trữ và thư mục gốc của phòng ban mình.
- **Employee:** Nhân viên thông thường trực thuộc một phòng ban, được cấp quota cá nhân để tải lên và quản lý tệp tin.
- **Department:** Phòng ban do Trưởng phòng tự thiết lập hoặc được TenantAdmin phân công.
- **Department StorageQuotaBytes:** Tổng hạn mức dung lượng mà TenantAdmin phân bổ cho phòng ban.
- **UserStorageQuota:** Hạn mức lưu trữ cá nhân của một người dùng cụ thể (`QuotaBytes`, `UsedBytes`).
- **AdminPersonalQuota:** Hạn mức lưu trữ cá nhân mà Trưởng phòng tự trích từ tổng quota phòng ban để giữ lại cho chính mình sử dụng khi chạy lệnh `SetupDepartment`.
- **Department Root Folder:** Thư mục gốc cấp cao nhất của phòng ban (`ParentFolderId = null`), tự động khởi tạo khi phòng ban được thiết lập để làm nơi chứa tài liệu chung.
- **Department-level Isolation:** Nguyên tắc bảo mật bảo đảm Trưởng phòng chỉ quản lý các đối tượng thuộc đúng `DepartmentId` của mình.
- **Tenant-level Isolation:** Nguyên tắc bảo mật bảo đảm người dùng ở mọi vai trò chỉ được thao tác trong phạm vi `TenantId` của tổ chức mình.
- **Fail-Closed Authorization:** Cơ chế an ninh mặc định từ chối khi thông tin định danh (TenantId hoặc DepartmentId) bị thiếu hoặc không xác định được.
- **EmailVerificationToken (EmployeeInvitation):** Token kích hoạt tài khoản gửi qua email để nhân viên mới tự tạo mật khẩu lần đầu (thời hạn 48 giờ).

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi loại trừ)

### Trong phạm vi (In-Scope)
- Tự thiết lập phòng ban (`SetupDepartment`): Chuyển quota tạm từ "ví" thành quota phòng ban, tạo mã và tên phòng ban (kèm kiểm tra schema regex), trích quota cá nhân cho Trưởng phòng, tạo thư mục gốc phòng ban.
- Tuyển dụng & Mời nhân viên (`CreateEmployee`): Tạo trực tiếp hoặc gửi email mời kích hoạt (token 48h), cấp quota ban đầu từ quỹ phòng ban, kiểm tra dữ liệu đầu vào bằng FluentValidation (chặn quota âm, kiểm tra email).
- Điều phối hạn mức cá nhân (`AllocateUserStorageQuota`): Tăng, giảm quota nhân viên với giao dịch `Serializable`, bảo vệ dữ liệu đã sử dụng, kiểm tra cô lập 2 chiều (Tenant và Department), che giấu thông điệp ngoại lệ DB.
- Thống kê hạn mức phòng ban (`GetDepartmentUsersStorageQuota`): Xem tổng quan dung lượng phòng ban, tổng quota đã chia cho nhân sự và dung lượng còn trống chưa phân bổ với cơ chế Fail-Closed bảo vệ quyền riêng tư.
- Ghi nhận `AuditLog` cho toàn bộ các thao tác quản trị nhân sự và điều chỉnh dung lượng.

### Loại trừ (Out-of-Scope)
- Thay đổi tổng hạn mức của phòng ban (`Department.StorageQuotaBytes`) — thuộc thẩm quyền `TenantAdmin`.
- Bổ nhiệm hoặc thay thế Trưởng phòng — thuộc thẩm quyền `TenantAdmin`.
- Xem hoặc can thiệp vào phòng ban khác trong cùng Tenant — vi phạm nguyên tắc cô lập phòng ban.
- Xem hoặc can thiệp vào phòng ban thuộc Tenant khác — vi phạm nghiêm trọng nguyên tắc cô lập tổ chức (Cross-Tenant Violation).
- Phê duyệt quy trình thanh toán hoặc cấp phát Storage Provider — thuộc thẩm quyền `SystemAdmin`.

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Phân hệ quản lý nhân sự trực tiếp và điều phối hạn mức lưu trữ cá nhân tới người dùng cuối; yêu cầu bảo đảm toàn vẹn hạn mức phân cấp 3 tầng (Tenant → Department → User), chống Race Condition khi cấp phát đĩa và bảo đảm cô lập dữ liệu cấp phòng ban.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Trưởng phòng có thể tự thiết lập nhiều hơn 1 phòng ban không? | Không; một tài khoản DepartmentAdmin chỉ được phép thiết lập và gắn với duy nhất 1 phòng ban (`user.DepartmentId == null` là điều kiện tiên quyết). | `RESOLVED` |
| 2 | Khi Trưởng phòng thiết lập phòng ban mà không chỉ định quota cá nhân giữ lại thì mặc định là bao nhiêu? | Mặc định giữ lại `Min(DepartmentQuota, 1 GB)` cho cá nhân Trưởng phòng, phần còn lại dành để cấp cho nhân viên. | `RESOLVED` |
| 3 | Khi nhân viên rời phòng ban hoặc bị xóa, quota cá nhân và file của họ xử lý thế nào? | Khi chuyển phòng hoặc khóa tài khoản, hạn mức cá nhân được thu hồi về quỹ phòng ban; dữ liệu file được bảo lưu theo chính sách Soft-Delete để bảo toàn AuditLog. | `RESOLVED` |
| 4 | Trưởng phòng có thể xem file mật cá nhân của nhân viên trong phòng không? | Trưởng phòng chỉ quản lý dung lượng (Metadata Quota) và các thư mục chia sẻ chung của phòng ban; không tự ý truy cập file riêng tư của nhân viên trừ khi được chia sẻ quyền. | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Phân hệ `DepartmentAdmin` đóng vai trò là tầng quản trị cấp cơ sở gần với người dùng nhất. Phân hệ cung cấp 4 chức năng cốt lõi: Tự thiết lập phòng ban (từ ví quota tạm sang phòng ban chính thức kèm thư mục gốc), tạo/mời nhân viên (kèm cấp quota ban đầu), điều phối hạn mức lưu trữ cá nhân (với giao dịch `Serializable` và kiểm tra trần quota phòng ban), cùng truy vấn bảng tổng hợp hạn mức nhân viên trong phòng. Toàn bộ logic tuân thủ nghiêm ngặt nguyên tắc cô lập phòng ban và kiểm toán an ninh.
- **Đối chiếu:** Khớp 100% với hiện trạng mã nguồn trong `CleanArchCqrs.Application/Features/DepartmentAdmin`, các Controller tại `CleanArchCqrs.API/Controllers/Department/`, và tài liệu `03_ROLE_DEPARTMENT_ADMIN.md`.

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Đề xuất:** Phê duyệt ngữ cảnh Phase 0 v1.2.0 cho phân hệ DepartmentAdmin: Loại bỏ hoàn toàn fallback hardcode/Tenant; đọc trực tiếp quota từ ví tạm `UserStorageQuota`; tách biệt các class Command/Query/Handler; và chuẩn hóa phân quyền policy `department.manage`.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản ngữ cảnh CONTEXT.md v1.2.0 cho phân hệ DepartmentAdmin.
- **Reviewed At:** 2026-10-14T00:30:00+07:00
- **Notes / Adjustments:** Phê duyệt toàn bộ cập nhật ngữ cảnh phân hệ DepartmentAdmin.
