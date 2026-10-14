# SDD Phase 0: Context Discovery — Delete User Feature

- **Module:** `SystemAdmin`
- **Feature:** `delete-user`
- **Scope:** Soft-Delete tài khoản người dùng, bảo vệ SystemAdmin duy nhất, thu hồi Refresh Tokens, và điều phối Idempotency.
- **Status:** `DRAFT`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp chức năng cho `SystemAdmin` (hoặc người dùng có quyền `user.manage`) xóa một tài khoản người dùng khỏi hệ thống thông qua cơ chế Soft-Delete (`IsActive = false`), đồng thời thu hồi toàn bộ Refresh Tokens và tăng `AuthorizationVersion` để chấm dứt các phiên làm việc đang hoạt động của người dùng đó.
- **WHY (Tại sao cần tính năng này):**
  - Cần ngừng quyền truy cập của người dùng đã nghỉ việc hoặc vi phạm chính sách bảo mật mà không làm hỏng toàn vẹn khóa ngoại trong cơ sở dữ liệu (các bảng `AuditLogs`, `FileEntries`, `FileShares`).
  - Bảo vệ an toàn vận hành: Ngăn chặn tuyệt đối việc xóa nhầm tài khoản `SystemAdmin` duy nhất trong toàn hệ sinh thái.
  - Ngăn ngừa hành vi lặp lệnh (Double mutation) bằng header `Idempotency-Key` qua `IAuthorizationMutationCoordinator`.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ranh giới nghiệp vụ: Xóa người dùng là Soft-Delete, không xóa vật lý bản ghi.
  - Phân định rõ: Xóa tài khoản `TenantAdmin` không đồng nghĩa với việc xóa tổ chức `Tenant`.
  - Quyền hạn bắt buộc: Quyền `user.manage` hoặc vai trò `SystemAdmin`.
- **Decision Owner:** Tech Lead & Security Architect (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Xóa cứng (Hard-delete) làm gãy liên kết khóa ngoại với lịch sử Audit Log và File | Thực hiện Soft-Delete: Cập nhật `IsActive = false` và giữ nguyên bản ghi thực thể `User`. |
| Nguy cơ xóa nhầm tài khoản SystemAdmin duy nhất dẫn tới tê liệt hệ thống | Chặn ngay ở tầng nghiệp vụ nếu tài khoản bị xóa là SystemAdmin duy nhất đang hoạt động. |
| Sau khi bị xóa, tài khoản vẫn dùng Refresh Token cũ để lấy Access Token mới | Thu hồi ngay lập tức toàn bộ Refresh Tokens và tăng `AuthorizationVersion` trong cùng transaction. |
| Bấm nhiều lần nút Xóa khi mạng lag gây xung đột dữ liệu | Yêu cầu bắt buộc header `Idempotency-Key` để điều phối qua Mutation Coordinator. |
| Thiếu phân quyền rõ ràng dẫn tới lỗi 403 hoặc lộ lỗ hổng bảo mật | Yêu cầu kiểm tra quyền `user.manage` và kiểm tra quyền quản trị cấp cao. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **Soft-Delete:** Đánh dấu bản ghi là không hoạt động (`IsActive = false`) thay vì xóa vật lý khỏi CSDL.
- **SystemAdmin:** Quản trị viên tối cao của toàn hệ thống DMS.
- **TenantAdmin:** Quản trị viên của một Tenant cụ thể.
- **Idempotency-Key:** Khóa định danh duy nhất của request từ Client gửi lên, giúp hệ thống nhận diện và xử lý an toàn các yêu cầu gửi lặp lại.
- **AuthorizationVersion:** Số phiên bản bảo mật của User, dùng để làm mất hiệu lực token đang lưu hành.
- **Mutation Coordinator:** Thành phần điều phối giao dịch ghi nhận thay đổi quyền và trạng thái bảo mật của hệ thống.

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi loại trừ)

### Trong phạm vi (In-Scope)
- Vô hiệu hóa tài khoản người dùng (`User.IsActive = false`).
- Thu hồi tất cả `RefreshToken` của tài khoản đó.
- Kiểm tra an toàn: Cấm xóa `SystemAdmin` duy nhất.
- Bắt buộc kiểm tra quyền `user.manage` và header `Idempotency-Key`.
- Tăng `AuthorizationVersion`.

### Loại trừ (Out-of-Scope)
- Xóa tổ chức Tenant (được quản lý riêng bởi use case `delete-tenant`).
- Xóa vật lý các tệp tin cá nhân của người dùng đã tải lên (các tệp tin vẫn tồn tại và tuân theo chính sách chia sẻ / quản lý của phòng ban và Tenant).

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Thao tác xóa tài khoản tác động trực tiếp tới danh tính, bảo mật và toàn vẹn dữ liệu hệ thống.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Khi xóa người dùng có xóa tệp tin của họ không? | Giữ nguyên tệp tin của người dùng để Tenant/Department tiếp tục kế thừa quyền sở hữu hoặc xử lý theo quy định nội bộ. | `RESOLVED` |
| 2 | Nếu xóa một tài khoản là TenantAdmin duy nhất của Tenant thì xử lý thế nào? | Cho phép xóa tài khoản người dùng đó, Tenant vẫn giữ nguyên trạng thái; SystemAdmin có thể gán quyền TenantAdmin cho người dùng khác sau đó. | `RESOLVED` |
| 3 | Thao tác xóa có thể khôi phục (Restore/Undelete) lại được không? | Có thể khôi phục thông qua tính năng `change-user-status` (`IsActive = true`) hoặc cập nhật người dùng. | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Feature `delete-user` xử lý Soft-Delete tài khoản người dùng trong `CleanArchCqrs.Application/Features/SystemAdmin/Commands/DeleteUser`. Đảm bảo bảo vệ SystemAdmin duy nhất, thu hồi Refresh Tokens, bảo toàn khóa ngoại và tuân thủ Idempotency.
- **Đối chiếu:** Hoàn toàn khớp với handler `DeleteUserCommandHandler.cs`, controller `UsersController.cs`, và tài liệu kiến trúc.

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Phê duyệt ngữ cảnh CONTEXT.md cho feature `delete-user`.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature delete-user.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
