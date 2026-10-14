# SDD Phase 0: Context Discovery — SystemAdmin Features

- **Module:** `SystemAdmin`
- **Scope:** 17 Features (`create-user`, `update-user`, `change-user-status`, `delete-user`, `assign-user-roles`, `reset-user-password`, `get-users-with-pagination`, `get-user-by-id`, `test-storage-provider-connection`, `connect-storage-provider`, `exchange-onedrive-code`, `delete-storage-provider`, `get-storage-providers`, `get-storage-provider-available-quota`, `allocate-tenant-storage-quota`, `get-tenant-storage-quota`, `get-audit-logs`)
- **Status:** `DRAFT`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp trung tâm điều hành cấp cao nhất (Super Administration) cho nền tảng Cloud-DMS để quản lý danh tính tài khoản toàn hệ sinh thái, điều phối tài nguyên lưu trữ đám mây (Local, Google Drive, OneDrive) và phân bổ hạn mức cho các tổ chức (Tenants), đồng thời giám sát an ninh thông qua truy vết nhật ký kiểm toán.
- **WHY (Tại sao cần tính năng này):**
  - Trong mô hình Multi-Tenant, cần có cơ chế quản trị tập trung độc lập với các TenantAdmin để khởi tạo tổ chức, điều tiết hạn mức dung lượng tránh cạn kiệt đĩa.
  - Cần bảo vệ an toàn cho thông tin xác thực đám mây (API tokens, OAuth2 refresh tokens) thông qua mã hóa đối xứng.
  - Ngăn ngừa sự cố vận hành nghiêm trọng: Mất quyền admin duy nhất, hoặc lộ lọt thông tin nhạy cảm.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ranh giới nghiệp vụ (Boundaries & Exclusions).
  - Thu thập đầy đủ thuật ngữ nghiệp vụ (Domain Glossary).
  - Phân loại và gán disposition cho các giả định/câu hỏi mở.
- **Decision Owner:** Product Owner & Tech Lead (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Khó khởi tạo Tenant mới đi kèm hạ tầng lưu trữ ngay từ đầu | Cho phép tạo đồng thời User, Tenant và gán Quota Provider chỉ trong một thao tác quản trị. |
| Nguy cơ khóa nhầm hoặc xóa nhầm tài khoản Admin duy nhất | Hệ thống tự động chặn mọi thao tác vô hiệu hóa/xóa nếu chỉ còn 1 SystemAdmin hoạt động. |
| Người dùng bị khóa hoặc đổi quyền nhưng token cũ vẫn truy cập được | Tự động thu hồi Refresh Token và tăng `AuthorizationVersion` để vô hiệu hóa token cũ. |
| Cấp phát dung lượng vượt quá dung lượng thực tế của Cloud | Tự động tính toán dung lượng khả dụng chưa phân bổ trước khi cho phép gán Quota. |
| Nguy cơ rò rỉ Client Secret / Refresh Token của Cloud Provider | Mã hóa bắt buộc bằng AES-256 trước khi lưu CSDL, không bao giờ gửi khóa giải mã ra API. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **SystemAdmin:** Quản trị viên tối cao của toàn hệ thống DMS.
- **Tenant:** Một tổ chức/doanh nghiệp thuê sử dụng hệ thống Cloud-DMS.
- **Department:** Phòng ban trực thuộc một Tenant cụ thể.
- **StorageProvider:** Nhà cung cấp dịch vụ lưu trữ vật lý (Local, Google Drive, Microsoft OneDrive).
- **StorageQuotaBytes:** Hạn mức dung lượng tối đa mà Tenant được phép lưu trữ trên một Provider.
- **StorageUsedBytes:** Dung lượng thực tế mà người dùng trong Tenant đã tải lên.
- **AvailableToAllocate:** Dung lượng còn trống thực tế của Provider sau khi trừ đi tổng quota đã cam kết cấp cho tất cả các Tenant.
- **AuthorizationVersion:** Số hiệu phiên bản quyền hạn của người dùng, dùng để làm mất hiệu lực các JWT Access Token đã phát hành khi có thay đổi bảo mật.
- **AuditLog:** Bản ghi nhật ký ghi lại hành vi của người dùng hoặc hệ thống để phục vụ kiểm toán an ninh.

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi loại trừ)

### Trong phạm vi (In-Scope)
- Vòng đời tài khoản người dùng xuyên tổ chức: Tạo, Sửa, Đổi trạng thái, Xóa mềm, Đặt lại mật khẩu, Gán vai trò/phòng ban.
- Kết nối, kiểm thử và quản lý Cloud Storage Provider.
- Điều phối hạn mức dung lượng giữa các Tenant.
- Truy vấn và lọc Audit Logs toàn hệ thống.

### Loại trừ (Out-of-Scope)
- Cổng thanh toán hóa đơn hoặc tự động trừ tiền thuê hàng tháng.
- Xem hoặc chỉnh sửa nội dung tài liệu mật của người dùng trong Tenant.
- Phân quyền chi tiết bên trong từng phòng ban của Tenant (thuộc thẩm quyền TenantAdmin).

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Phân hệ sở hữu các đặc quyền tối thượng của toàn bộ hệ thống, quản lý dữ liệu liên tổ chức và cấu hình bảo mật kết nối đám mây.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Khi xóa người dùng có xóa vĩnh viễn dữ liệu (Hard-delete) không? | Áp dụng Soft-Delete (`IsActive = false`) để bảo toàn tính toàn vẹn khóa ngoại của `AuditLogs` và `FileEntries`. | `RESOLVED` |
| 2 | Nếu Cloud Provider có dung lượng không giới hạn hoặc chưa sync thì xử lý thế nào? | Fallback dung lượng mặc định 15 GB để phục vụ tính toán giao diện cho đến khi sync thực tế. | `RESOLVED` |
| 3 | Khi Tenant chuyển sang Provider khác, dữ liệu cũ xử lý ra sao? | Tạm thời unbind và giữ nguyên dữ liệu trên Provider cũ; nghiệp vụ migration dữ liệu sẽ xử lý ở phân hệ riêng. | `DEFERRED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Phân hệ `SystemAdmin` gồm 17 Use Case cung cấp các khả năng quản lý tài khoản, hạ tầng lưu trữ đám mây và hạn mức tổ chức. Tính năng không can thiệp vào nghiệp vụ tài liệu nội bộ của Tenant và tuân thủ các nguyên tắc bảo mật: Không rò rỉ secret, bảo vệ Admin duy nhất, thu hồi token khi đổi quyền.
- **Đối chiếu:** Khớp 100% với hiện trạng mã nguồn trong `CleanArchCqrs.Application/Features/SystemAdmin` và tài liệu kiến trúc `ARCHITECTURE.md`.

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Phê duyệt ngữ cảnh Phase 0 để làm cơ sở cho toàn bộ 17 feature con trong phân hệ SystemAdmin.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho phân hệ SystemAdmin.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
