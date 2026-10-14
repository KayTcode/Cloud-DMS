# SDD Phase 0: Context Discovery — Create Department Admin

- **Module:** `TenantAdmin`
- **Feature Slug:** `create-department-admin`
- **Version:** `1.2.0`
- **Scope:** Nghiệp vụ bổ nhiệm Trưởng phòng (`DepartmentAdmin`) trong tổ chức (Tenant)
- **Status:** `APPROVED`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp chức năng cho Quản trị viên Tổ chức (`TenantAdmin`) bổ nhiệm Trưởng phòng (`DepartmentAdmin`) mới.
  - **Quy trình Kích hoạt qua Thư mời (Invitation-First / Zero-Direct-Password):** TenantAdmin **không cần và không được tự đặt mật khẩu** cho Trưởng phòng. Hệ thống tự động khởi tạo tài khoản ở trạng thái chưa kích hoạt (`IsActive = false`, `EmailConfirmed = false`), sinh mật khẩu ngẫu nhiên tạm thời, tạo mã xác thực `EmailVerificationToken` (hiệu lực 48 giờ) và gửi thư mời kích hoạt qua email để Trưởng phòng tự thiết lập mật khẩu cá nhân (tương tự như quy trình `SystemAdmin` tạo người dùng/tổ chức mới).
  - **Cơ chế tái tạo email đã xóa mềm (Re-creation / Soft-Delete):** Cho phép bổ nhiệm lại với email đã từng tồn tại trong hệ thống nếu tài khoản trước đó đã ở trạng thái bị xóa mềm (`IsActive = false`), đồng bộ với cơ chế của use case `system-admin/create-user` nhằm giảm thiểu việc cạn kiệt email thực tế trong quá trình vận hành và kiểm thử liên tục.
  - **Hỗ trợ 2 mô hình phân bổ phòng ban:** Gán trực tiếp vào phòng ban có sẵn (`DepartmentId`) hoặc mô hình "Admin-First / Self-Setup" (cấp trước ngân sách quota vào `UserStorageQuota` lấy từ dung lượng khả dụng của Tenant để Trưởng phòng tự khởi tạo phòng ban sau).
- **WHY (Tại sao cần tính năng này):**
  - **Bảo mật và Phân quyền cá nhân:** Loại bỏ rủi ro lộ mật khẩu hoặc việc quản trị viên cấp trên biết trước mật khẩu của cấp dưới. Trưởng phòng hoàn toàn tự chủ sở hữu tài khoản và mật khẩu của mình ngay từ lần đăng nhập đầu tiên thông qua quy trình `AcceptInvitation`.
  - **Thống nhất trải nghiệm quản trị:** Đồng bộ luồng khởi tạo người dùng giữa các cấp quản trị (`SystemAdmin` tạo `TenantAdmin`, `TenantAdmin` tạo `DepartmentAdmin`).
  - **Tối ưu hóa quản lý dữ liệu người dùng:** Tránh xung đột Unique Index khi kiểm thử và cho phép tái sử dụng email của tài khoản đã xóa mềm mà không làm ảnh hưởng tính toàn vẹn dữ liệu.
  - **Cô lập Multi-Tenancy:** Đảm bảo ranh giới cô lập tuyệt đối giữa các tổ chức và phòng ban.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ranh giới nghiệp vụ loại bỏ hoàn toàn việc nhập mật khẩu trực tiếp từ TenantAdmin.
  - Thống nhất cơ chế giải phóng email của tài khoản xóa mềm để cho phép tái cấp phát.
  - Thu thập đầy đủ thuật ngữ nghiệp vụ (Domain Glossary) và giải quyết các câu hỏi mở.
- **Decision Owner:** Product Owner & Tech Lead (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| TenantAdmin phải tự đặt và bàn giao mật khẩu thủ công cho Trưởng phòng, tiềm ẩn nguy cơ lộ thông tin đăng nhập và vi phạm chính sách bảo mật | **100% Invitation-First:** Bỏ trường nhập mật khẩu ở giao diện TenantAdmin. Hệ thống tự sinh token mời 48 giờ gửi qua email; Trưởng phòng tự kích hoạt và đặt mật khẩu lần đầu qua lệnh `AcceptInvitation`. |
| Email của tài khoản cũ đã bị xóa mềm (`IsActive = false`) không thể sử dụng lại để tạo tài khoản mới do vi phạm Unique Index `Users.Email` | **Re-creation of Soft-Deleted Accounts:** Khi phát hiện email trùng với tài khoản đã xóa mềm (`IsActive = false`), hệ thống tự giải phóng email cũ sang dạng `{email}__deleted_{timestamp}` và cho phép tạo tài khoản mới thành công (đồng bộ với `system-admin/create-user`). |
| Trưởng phòng mới chưa có phòng ban ngay khi gia nhập tổ chức | Hỗ trợ mô hình "Admin-First / Self-Setup": Cấp trước quota vào "ví tạm" (`UserStorageQuota`) từ dung lượng khả dụng của Tenant (`AvailableInTenant`). Nếu TenantAdmin không nhập hạn mức cụ thể, hệ thống tự động cấp toàn bộ dung lượng khả dụng của Tenant, không để Quota bị gán bằng 0. |
| Xung đột khi một phòng ban có nhiều hơn 1 Trưởng phòng | Ràng buộc chặt chẽ 1:1 — Mỗi phòng ban chỉ được phép có duy nhất 1 Trưởng phòng hoạt động. |
| Nguy cơ cấp phát vượt quá hạn mức dung lượng khả dụng của tổ chức | Kiểm tra trần Quota nghiêm ngặt trước khi cấp hạn mức cho Trưởng phòng (dù là gán vào phòng ban hay Self-Setup). |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **TenantAdmin:** Quản trị viên cấp tổ chức, thực hiện bổ nhiệm Trưởng phòng trong phạm vi tổ chức của mình.
- **DepartmentAdmin:** Trưởng phòng ban trực thuộc Tenant, nhận thư mời kích hoạt tài khoản và quản lý nhân viên/tệp tin phòng ban.
- **EmailVerificationToken:** Mã xác thực bảo mật (chuỗi GUID kép 64 ký tự), hạn dùng 48 giờ, dùng 1 lần để kích hoạt tài khoản qua use case `AcceptInvitation`.
- **Soft-Deleted User Re-creation:** Cơ chế giải phóng email của bản ghi người dùng đã vô hiệu hóa (`IsActive = false`) sang định dạng `{email}__deleted_{timestamp}` nhằm cho phép tái tạo tài khoản mới với email gốc mà không vi phạm ràng buộc Unique Index.
- **Self-Setup (Admin-First):** Quy trình tạo tài khoản Trưởng phòng trước khi có phòng ban (`DepartmentId = null`); hạn mức ban đầu được cấp tạm vào `UserStorageQuota` để đem sang thiết lập phòng ban sau qua lệnh `SetupDepartment`.
- **AvailableInTenant:** Dung lượng còn trống thực tế của Tenant chưa phân bổ (`TenantQuota - Sum(DeptsQuota + PendingAdminsQuota)`).

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi loại trừ)

### Trong phạm vi (In-Scope)
- Bổ nhiệm Trưởng phòng (`DepartmentAdmin`) theo quy trình **bắt buộc gửi thư mời qua Email (Invitation-Only)**.
- Tự động sinh mật khẩu tạm thời ngẫu nhiên được băm an toàn; tài khoản khởi tạo với `IsActive = false`, `EmailConfirmed = false`.
- Sinh `EmailVerificationToken` loại `DepartmentAdminInvitation` có thời hạn 48 giờ và gửi qua `IEmailService`.
- Cơ chế cho phép tái tạo tài khoản với email trùng với tài khoản đã bị xóa mềm (`IsActive = false`).
- Hỗ trợ cả 2 mô hình: Gán vào phòng ban có sẵn hoặc cấp quota tạm thời (Self-Setup).
- Ràng buộc tỷ lệ 1-1: Mỗi phòng ban chỉ có tối đa 1 Trưởng phòng.
- Ghi nhận `AuditLog` cho hành vi mời Trưởng phòng.

### Loại trừ (Out-of-Scope)
- **TenantAdmin tự đặt mật khẩu cho Trưởng phòng:** Đã loại bỏ hoàn toàn khỏi nghiệp vụ này.
- Kích hoạt tài khoản và đặt mật khẩu lần đầu: Thực hiện qua use case `AcceptInvitation` (thuộc phân hệ Auth/Users).
- Quản lý hạ tầng đám mây vật lý (`StorageProvider`) — thuộc thẩm quyền `SystemAdmin`.
- Điều phối quota chi tiết cho nhân viên trong phòng ban — thuộc thẩm quyền `DepartmentAdmin`.

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Nghiệp vụ cấp phát tài khoản quản trị cấp phòng ban, phân bổ tài nguyên lưu trữ và xác thực danh tính qua email token trong môi trường Multi-Tenancy; yêu cầu bảo mật cao, kiểm soát chặt chẽ trạng thái kích hoạt và tính toàn vẹn dữ liệu.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Mỗi phòng ban có thể có bao nhiêu Trưởng phòng (DepartmentAdmin)? | Mỗi phòng ban chỉ được phép có duy nhất 1 Trưởng phòng hoạt động. | `RESOLVED` |
| 2 | Khi Trưởng phòng được tạo ở chế độ Self-Setup, Quota tạm thời được lưu ở đâu? | Lưu trữ trong bảng `UserStorageQuota` của người dùng; khi Trưởng phòng chạy lệnh `SetupDepartment`, số quota này sẽ được chuyển thành Quota của phòng ban mới. | `RESOLVED` |
| 3 | Nếu dịch vụ gửi email (SMTP/Gmail) gặp sự cố gián đoạn thì có hủy việc tạo Trưởng phòng không? | Không hủy giao dịch; tài khoản và token vẫn được lưu an toàn trong CSDL để có thể gửi lại liên kết sau mà không làm mất dữ liệu. | `RESOLVED` |
| 4 | TenantAdmin có còn quyền tự nhập mật khẩu cho DepartmentAdmin không? | **Không.** Bãi bỏ hoàn toàn việc nhập mật khẩu trực tiếp. 100% tài khoản Trưởng phòng mới được tạo ở trạng thái chưa kích hoạt (`IsActive = false`) và gửi email mời kích hoạt kèm token 48h để người dùng tự thiết lập mật khẩu. | `RESOLVED` |
| 5 | Xử lý email trùng lặp với tài khoản đã bị xóa mềm (`IsActive = false`) như thế nào? | Cho phép tái tạo tài khoản đối với email đã bị xóa mềm bằng cách đổi tên email cũ sang `{email}__deleted_{timestamp}`, đồng bộ với usecase `system-admin/create-user`. | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Ngữ cảnh của feature `create-department-admin` đã được điều chỉnh toàn diện: Bãi bỏ luồng nhập mật khẩu thủ công từ `TenantAdmin`, chuyển đổi 100% sang mô hình Email Invitation (tài khoản tạo mới luôn có `IsActive = false` và gửi thư mời kèm token 48 giờ để Trưởng phòng tự kích hoạt qua `AcceptInvitation`). Đồng thời áp dụng cơ chế giải phóng email của các tài khoản đã bị xóa mềm (`IsActive = false`) để cho phép tái cấp phát tài khoản tương tự chuẩn của `system-admin/create-user`.
- **Đối chiếu:** Đồng bộ hoàn toàn với kiến trúc hệ thống và quy tắc tại `CleanArchCqrs.Application/Features/SystemAdmin/Commands/CreateUser/CreateUserCommandHandler.cs` và `CleanArchCqrs.Application/Users/Commands/AcceptInvitation/AcceptInvitationCommand.cs`.

---

## 8. Change Impact Record

- Changed intent, requirement, architecture, or task assumption:
  - Chuyển feature sang mô hình 100% Email Invitation (loại bỏ nhập mật khẩu thủ công); bổ sung cơ chế tái tạo tài khoản với email đã bị xóa mềm (`IsActive = false`) đồng bộ với `system-admin/create-user`.
  - Cập nhật EARS-03 (v1.2.0): Khi bổ nhiệm Trưởng phòng ở chế độ Self-Setup (`DepartmentId = null`), nếu không truyền hạn mức cụ thể, hệ thống tự động cấp toàn bộ dung lượng khả dụng của Tenant (`availableInTenant`) vào `UserStorageQuota` của Trưởng phòng, loại bỏ hoàn toàn lỗi gán Quota = 0.
- Feature Lock boundary affected: Scope của feature `create-department-admin` (Command DTO, Request DTO, Validator, Handler và Spec).
- Material state-change classification: Behavior & Contract Update (Spec v1.2.0).
- Downstream artifacts invalidated: `SPEC.md`, `PLAN.md`, `TASKS.md`, `CreateDepartmentAdminCommand`, `CreateDepartmentAdminRequest`, `CreateDepartmentAdminCommandValidator`, `CreateDepartmentAdminCommandHandler`.
- Required trace, test, and sync actions: Cập nhật `SPEC.md` lên v1.2.0; đồng bộ các bài kiểm thử và implementation liên quan.
- Review follow-up: `Architecture & Security Review Mandatory`.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Đề xuất:** Phê duyệt ngữ cảnh CONTEXT.md v1.2.0 cho feature `create-department-admin` (100% Email Invitation, tái tạo email đã xóa mềm, và tự động cấp quota khả dụng của Tenant khi Self-Setup theo EARS-03).

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản ngữ cảnh CONTEXT.md v1.2.0 cho feature create-department-admin.
- **Reviewed At:** 2026-10-14T00:30:00+07:00
- **Notes / Adjustments:** Phê duyệt toàn bộ cập nhật Self-Setup Quota allocation và bảo mật Invitation-First.
