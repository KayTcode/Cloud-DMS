# SDD Phase 0: Context Discovery — TenantAdmin Features

- **Module:** `TenantAdmin`
- **Scope:** 3 Core Features (`create-department-admin`, `allocate-department-storage-quota`, `get-departments`) và các luồng quản trị tổ chức liên quan
- **Status:** `DRAFT`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp cổng quản trị cấp tổ chức (Organization Administration) cho Quản trị viên Tenant (`TenantAdmin`) để quản lý cấu trúc phòng ban, bổ nhiệm và quản lý Trưởng phòng (`DepartmentAdmin`), phân bổ và kiểm soát hạn mức dung lượng lưu trữ (`StorageQuota`) từ tổng quỹ của tổ chức xuống từng phòng ban, đồng thời giám sát tình hình sử dụng tài nguyên đĩa và nhân sự trong tổ chức.
- **WHY (Tại sao cần tính năng này):**
  - Trong mô hình Multi-Tenancy của Cloud-DMS, dữ liệu giữa các tổ chức phải được cô lập triệt để. `TenantAdmin` cần quyền tự chủ điều hành tổ chức của mình mà không cần phụ thuộc vào `SystemAdmin`.
  - Kiểm soát tài nguyên lưu trữ theo cấu trúc phân cấp (Tenant → Department → User), ngăn chặn tình trạng một phòng ban dùng tràn lan làm cạn kiệt dung lượng của toàn tổ chức.
  - Hỗ trợ mô hình làm việc linh hoạt: Cho phép bổ nhiệm Trưởng phòng vào phòng ban có sẵn hoặc áp dụng mô hình "Admin-First / Self-Setup" (cấp trước ngân sách quota để Trưởng phòng tự thiết lập phòng ban sau).
  - Đảm bảo an ninh và phân quyền chặt chẽ (RBAC, IDOR Protection, Audit Trail) cho mọi thao tác cấu hình tổ chức.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ranh giới nghiệp vụ (Boundaries & Exclusions) và ranh giới cô lập tổ chức (Multi-Tenant Isolation).
  - Thu thập đầy đủ thuật ngữ nghiệp vụ (Domain Glossary) về phân cấp Quota và tài khoản.
  - Phân loại và gán disposition cho các giả định/câu hỏi mở.
- **Decision Owner:** Product Owner & Tech Lead (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Trưởng phòng mới chưa có phòng ban ngay khi gia nhập tổ chức | Hỗ trợ mô hình "Admin-First / Self-Setup": Cấp trước quota vào "ví tạm" (`UserStorageQuota`) để Trưởng phòng tự khởi tạo phòng ban sau. |
| Nguy cơ hạ quota của phòng ban dẫn đến mất dữ liệu hoặc làm hỏng dữ liệu người dùng | Áp dụng cơ chế kiểm tra 3 lớp an toàn: Không hạ thấp hơn dung lượng đã dùng, không hạ thấp hơn tổng quota đã chia cho nhân viên, không vượt quota trống của Tenant. |
| Xung đột cấp phát quota khi nhiều quản trị viên thao tác đồng thời (Race Condition) | Đóng gói nghiệp vụ điều phối quota trong Database Transaction với mức cô lập `Serializable`. |
| Nguy cơ vi phạm an toàn dữ liệu giữa các tổ chức (IDOR / Multi-Tenant Leakage) | Mọi truy vấn và lệnh cập nhật bắt buộc phải xác thực ràng buộc `TenantId` từ JWT Token và CSDL. |
| Quy trình mời Trưởng phòng rườm rà, dễ lộ mật khẩu mặc định | Hỗ trợ cơ chế gửi Email Invitation bảo mật kèm token một lần có thời hạn 48 giờ để Trưởng phòng tự thiết lập mật khẩu ban đầu. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **TenantAdmin:** Quản trị viên cấp tổ chức/doanh nghiệp, chịu trách nhiệm quản lý phòng ban và nhân sự trong phạm vi Tenant của mình.
- **DepartmentAdmin:** Trưởng phòng ban trực thuộc Tenant, chịu trách nhiệm quản lý nhân viên và tệp tin trong phòng.
- **Tenant:** Một tổ chức/doanh nghiệp thuê sử dụng hệ sinh thái Cloud-DMS.
- **Department:** Đơn vị phòng ban thuộc một Tenant cụ thể.
- **Tenant StorageQuotaBytes:** Tổng hạn mức dung lượng mà SystemAdmin đã cấp cho Tenant từ Storage Provider.
- **Department StorageQuotaBytes:** Hạn mức dung lượng tối đa mà TenantAdmin phân bổ cho một phòng ban từ quỹ của Tenant.
- **Department StorageUsedBytes:** Tổng dung lượng thực tế mà các thành viên trong phòng ban đã tải lên.
- **TotalAllocatedToUsersBytes:** Tổng dung lượng mà Trưởng phòng đã chia cho các thành viên trong phòng ban đó.
- **AvailableInTenant:** Dung lượng còn trống thực tế của Tenant chưa phân bổ cho bất kỳ phòng ban hay Trưởng phòng chờ thiết lập nào (`TenantQuota - Sum(DeptsQuota + PendingAdminsQuota)`).
- **Self-Setup (Admin-First):** Quy trình tạo tài khoản Trưởng phòng trước khi có phòng ban; hạn mức được cấp tạm thời vào `UserStorageQuota` để đem sang thiết lập phòng ban sau.
- **EmailVerificationToken:** Mã xác thực gửi qua email để kích hoạt tài khoản và đặt mật khẩu lần đầu.

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi loại trừ)

### Trong phạm vi (In-Scope)
- Bổ nhiệm Trưởng phòng (`DepartmentAdmin`): Tạo trực tiếp hoặc gửi thư mời kích hoạt qua Email, hỗ trợ gán phòng ban có sẵn hoặc cấp quota tạm thời (Self-Setup).
- Điều phối hạn mức lưu trữ phòng ban (`AllocateDepartmentStorageQuota`): Cấp mới, nâng hạn mức hoặc thu hồi quota với 3 lớp kiểm tra an toàn và bảo vệ giao dịch `Serializable`.
- Truy vấn thông tin phòng ban (`GetDepartments`): Lấy danh sách phòng ban kèm cơ cấu nhân sự, Trưởng phòng đại diện, hạn mức phân bổ và dung lượng sử dụng.
- Kích hoạt tài khoản TenantAdmin thông qua lời mời từ SystemAdmin (`AcceptInvitation`).
- Ghi nhận AuditLog đầy đủ cho các hành vi thay đổi nhân sự và điều phối hạn mức trong tổ chức.

### Loại trừ (Out-of-Scope)
- Quản lý hạ tầng đám mây vật lý (`StorageProvider`: GDrive, OneDrive, Local) — thuộc thẩm quyền `SystemAdmin`.
- Điều phối quota chi tiết cho từng nhân viên (`Employee`) trong phòng ban — thuộc thẩm quyền `DepartmentAdmin`.
- Quản lý tệp tin và phân quyền tài liệu chi tiết trong phòng ban — thuộc thẩm quyền `DepartmentAdmin` và `Employee`.
- Thanh toán, nâng cấp gói cước tổ chức với nhà cung cấp nền tảng.

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Phân hệ điều phối tài nguyên lưu trữ và nhân sự cốt lõi của tổ chức trong môi trường Multi-Tenancy; yêu cầu bảo đảm toàn vẹn hạn mức lưu trữ, chống race condition và ngăn chặn rò rỉ chéo dữ liệu giữa các Tenant.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Mỗi phòng ban có thể có bao nhiêu Trưởng phòng (DepartmentAdmin)? | Mỗi phòng ban chỉ được phép có duy nhất 1 Trưởng phòng hoạt động để tránh xung đột quản trị quota và nhân sự. | `RESOLVED` |
| 2 | Khi Trưởng phòng được tạo ở chế độ Self-Setup, Quota tạm thời được lưu ở đâu? | Lưu trữ trong bảng `UserStorageQuota` của người dùng; khi Trưởng phòng chạy lệnh `SetupDepartment`, số quota này sẽ được chuyển thành Quota của phòng ban mới. | `RESOLVED` |
| 3 | Khi TenantAdmin muốn giải thể một phòng ban thì dữ liệu quota và file xử lý thế nào? | Tạm thời không cho phép xóa cứng phòng ban nếu còn dữ liệu hoặc thành viên; luồng thu hồi nhân sự và chuyển đổi phòng ban được xử lý ở sprint tiếp theo. | `DEFERRED` |
| 4 | Nếu dịch vụ gửi email (SMTP/Gmail) gặp sự cố gián đoạn thì có hủy việc tạo Trưởng phòng không? | Không hủy giao dịch; tài khoản và token vẫn được lưu an toàn trong CSDL để có thể gửi lại liên kết sau. | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Phân hệ `TenantAdmin` đóng vai trò là tầng quản trị trung gian giữa SystemAdmin và DepartmentAdmin. Phân hệ cung cấp các chức năng: Bổ nhiệm Trưởng phòng (hỗ trợ cả gán sẵn và Self-Setup qua email invitation), điều phối quota lưu trữ phòng ban (với cơ chế giao dịch `Serializable` và 3 lớp kiểm tra an toàn), cùng truy vấn cấu trúc phòng ban toàn tổ chức. Toàn bộ xử lý tuân thủ ranh giới Multi-Tenancy cô lập tuyệt đối và ghi nhật ký kiểm toán.
- **Đối chiếu:** Khớp 100% với hiện trạng mã nguồn trong `CleanArchCqrs.Application/Features/TenantAdmin`, `CleanArchCqrs.API/Controllers/Tenant`, và tài liệu hướng dẫn `02_ROLE_TENANT_ADMIN.md`.

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Phê duyệt ngữ cảnh Phase 0 cho feature con Get Departments.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature get-departments.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
