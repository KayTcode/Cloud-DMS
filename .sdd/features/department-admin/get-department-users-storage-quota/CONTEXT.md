# SDD Phase 0: Context Discovery — Get Department Users Storage Quota

- **Feature Slug:** `get-department-users-storage-quota`
- **Module:** `DepartmentAdmin`
- **Scope:** Truy vấn báo cáo tổng hợp hạn mức lưu trữ phòng ban và danh sách chi tiết hạn mức, số lượng tệp tin của từng nhân viên
- **Status:** `REVISED`
- **Spec Depth Baseline:** `DETAILED`

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp API và báo cáo thống kê cho Trưởng phòng (`DepartmentAdmin`) và `TenantAdmin` xem bức tranh tổng thể về tình hình sử dụng dung lượng lưu trữ của phòng ban, bao gồm:
  1. Tổng hạn mức phòng ban được TenantAdmin cấp (`DepartmentQuotaBytes`).
  2. Tổng dung lượng thực tế phòng ban đã sử dụng (`DepartmentUsedBytes`).
  3. Tổng hạn mức đã phân bổ cho các nhân viên (`TotalAllocatedToUsersBytes`).
  4. Dung lượng phòng ban còn lại chưa phân bổ (`UnallocatedDepartmentQuotaBytes`).
  5. Danh sách chi tiết từng nhân viên active kèm: Tên, Email, Quota được cấp, Dung lượng đã dùng, Dung lượng khả dụng, Tỷ lệ sử dụng phần trăm và **Số lượng tệp tin hợp lệ đang lưu trữ (`FileCount`)**.
- **WHY (Tại sao cần tính năng này):**
  - **Minh bạch tài nguyên:** Giúp Trưởng phòng có số liệu trực quan và chính xác về việc ai đang sử dụng bao nhiêu dung lượng và đang lưu trữ bao nhiêu tệp tin để có kế hoạch điều phối, tăng/giảm hạn mức hợp lý.
  - **Khắc phục điểm mù số lượng tệp tin:** Trước đây giao diện chưa thống kê hoặc hardcode số lượng tệp tin là 0, dẫn đến việc Trưởng phòng không đánh giá được hành vi lưu trữ của nhân sự (ví dụ: dung lượng ít nhưng lưu hàng nghìn file rác nhỏ hoặc ngược lại).
  - **Bảo mật phân quyền & Cô lập dữ liệu (Fail-Closed Isolation):** Đảm bảo Trưởng phòng chỉ xem được dữ liệu của phòng ban mình, ngăn chặn triệt để rò rỉ dữ liệu chéo phòng ban và chéo tổ chức (Multi-Tenant Isolation).
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ ranh giới nghiệp vụ của việc thống kê hạn mức và đếm số lượng tệp tin.
  - Chuẩn hóa thuật ngữ nghiệp vụ (Domain Glossary) bao gồm chỉ số `FileCount`.
  - Phân tích rủi ro bảo mật Fail-Closed đối với ngữ cảnh gọi API.
- **Decision Owner:** Product Owner & Tech Lead (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Trưởng phòng vào giao diện quản lý nhưng số lượng tệp tin của nhân viên không hiển thị hoặc luôn hiển thị 0 | Hệ thống tự động đếm chính xác số lượng tệp tin hợp lệ (`!IsDeleted`) của từng nhân sự từ bảng `FileEntries` và trả về `FileCount`. |
| Trưởng phòng không nắm được phòng ban còn bao nhiêu dung lượng chưa chia để cấp cho người mới | Tính toán và hiển thị rõ ràng chỉ số `UnallocatedDepartmentQuotaBytes = Max(0, DepartmentQuotaBytes - TotalAllocatedToUsersBytes)`. |
| Nguy cơ rò rỉ thông tin nhân sự và dung lượng giữa các phòng ban trong cùng công ty | Áp dụng nguyên tắc Fail-Closed: Bắt buộc đối chiếu `callerDeptId == department.Id` từ JWT Token; từ chối ngay lập tức nếu không khớp hoặc thiếu `DepartmentId`. |
| TenantAdmin của công ty khác dòm ngó dữ liệu phòng ban | Bắt buộc đối chiếu `callerTenantId == department.TenantId` (Multi-Tenant Isolation). |
| Truy vấn chậm khi phòng ban có nhiều nhân viên và số lượng tệp tin lớn | Sử dụng truy vấn gom nhóm tối ưu `GroupBy(f => f.OwnerId)` trên Database thay vì duyệt từng phần tử trong bộ nhớ. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **DepartmentAdmin:** Trưởng phòng ban, người có quyền xem báo cáo hạn mức và điều phối dung lượng cho các thành viên trong phòng ban của mình.
- **Department StorageQuotaBytes:** Tổng hạn mức dung lượng tối đa mà TenantAdmin phân bổ cho phòng ban.
- **DepartmentUsedBytes:** Tổng dung lượng thực tế mà tất cả các nhân viên trong phòng ban đã sử dụng (`Sum(UserStorageQuota.UsedBytes)`).
- **TotalAllocatedToUsersBytes:** Tổng hạn mức dung lượng đã trích ra để cấp cho các thành viên trong phòng (`Sum(UserStorageQuota.QuotaBytes)`).
- **UnallocatedDepartmentQuotaBytes:** Quỹ hạn mức còn lại của phòng ban chưa phân bổ cho nhân viên nào (`Max(0, DepartmentQuotaBytes - TotalAllocatedToUsersBytes)`).
- **UserStorageQuota:** Hạn mức lưu trữ cá nhân của nhân viên (`QuotaBytes`, `UsedBytes`, `AvailableBytes`, `UsagePercentage`).
- **FileCount:** Tổng số lượng tệp tin đang còn hiệu lực (chưa bị xóa mềm, `!IsDeleted`) thuộc quyền sở hữu của nhân viên (`OwnerId == user.Id`).
- **Fail-Closed Department Isolation:** Nguyên tắc bảo mật mặc định từ chối (`400 Bad Request` / `403 Forbidden`) nếu caller không cung cấp đủ thông tin phòng ban hợp lệ hoặc cố tình truy cập phòng ban khác.

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi loại trừ)

### Trong phạm vi (In-Scope)
- Truy vấn tổng hợp thông tin hạn mức phòng ban (`DepartmentQuotaBytes`, `DepartmentUsedBytes`, `TotalAllocatedToUsersBytes`, `UnallocatedDepartmentQuotaBytes`).
- Thống kê danh sách toàn bộ người dùng active trong phòng ban kèm các chỉ số: Hạn mức cấp, dung lượng đã dùng, dung lượng còn trống, tỷ lệ sử dụng và **số lượng tệp tin hợp lệ (`FileCount`)**.
- Áp dụng kiểm soát truy cập nghiêm ngặt: TenantAdmin xem được phòng ban thuộc tổ chức mình; DepartmentAdmin chỉ xem được phòng ban của chính mình; SystemAdmin toàn quyền.

### Loại trừ (Out-of-Scope)
- Thay đổi hạn mức của nhân viên (thuộc feature `allocate-user-storage-quota`).
- Tạo mới hoặc xóa nhân viên (thuộc feature `create-employee` / `manage-employee`).
- Xem nội dung chi tiết hoặc danh sách tên từng tệp tin của nhân viên (thuộc phân hệ Drive/FileExplorer và đảm bảo tính riêng tư của người dùng).
- Xuất báo cáo hạn mức ra file Excel/PDF (thuộc phân hệ Export chuyên biệt).

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Tính năng cung cấp số liệu tài nguyên cốt lõi cho cấp quản lý phòng ban; yêu cầu số liệu chính xác tuyệt đối, hiệu năng cao và tuân thủ nghiêm ngặt bảo mật Fail-Closed cô lập phòng ban và Multi-Tenant.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Số lượng tệp tin (`FileCount`) có bao gồm các tệp tin trong thùng rác (đã xóa mềm `IsDeleted = true`) không? | Không; chỉ tính các tệp tin hợp lệ đang hoạt động (`!IsDeleted`) vì chỉ các file này mới chiếm dụng tài nguyên hoạt động thực tế của người dùng. | `RESOLVED` |
| 2 | Nhân viên chưa tải lên tệp tin nào thì `FileCount` trả về giá trị gì? | Trả về `0` (không trả về null). | `RESOLVED` |
| 3 | Người dùng không có `DepartmentId` trong token (ví dụ tài khoản chưa được gán phòng ban) gọi API này thì xử lý thế nào? | Áp dụng Fail-Closed: Lập tức từ chối với thông báo "Bạn không có quyền xem thông tin hạn mức của phòng ban này." | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Feature con `get-department-users-storage-quota` đảm nhiệm vai trò cung cấp dashboard/báo cáo hạn mức lưu trữ và số lượng tệp tin cho Trưởng phòng ban. Dữ liệu trả về bao gồm thông tin tổng hợp của phòng ban và danh sách chi tiết từng nhân viên kèm số lượng tệp tin (`FileCount`) được tổng hợp bằng truy vấn `GroupBy` tối ưu trên `FileEntries`. Hệ thống thực thi triệt để Fail-Closed Department Isolation và Multi-Tenant Isolation.
- **Đối chiếu:** Khớp 100% với mã nguồn Backend trong `CleanArchCqrs.Application/Features/DepartmentAdmin/Queries/GetDepartmentUsersStorageQuota` và giao diện Frontend tại `Cloud-DMS-FE/src/features/department-admin/DepartmentAdminPortal.tsx`.

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Phê duyệt ngữ cảnh Phase 0 CONTEXT.md cho feature con `get-department-users-storage-quota`, cập nhật đồng bộ các chỉ số thống kê dung lượng và số lượng tệp tin (`FileCount`).

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature get-department-users-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING HUMAN REVIEW`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision

