# SDD Phase 2: Architecture Plan — Department Admin Real Data Binding

- **Feature Slug:** `department-admin`
- **Module:** `DepartmentAdmin`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Inherited Spec Depth:** `DETAILED`
- **Risk Posture:** `HIGH` (Quản lý hạn mức lưu trữ cá nhân, cô lập cấp phòng ban, khởi tạo cơ cấu tổ chức)

---

## 1. Architectural Approach & Pattern

- **Kiến trúc áp dụng:** Clean Architecture & CQRS, kết hợp React Context Pattern và API Adapter Pattern trên Frontend SPA.
- **Nguyên tắc định hướng:**
  1. *Zero-Mock Policy (Thực tế hóa 100%):* Loại bỏ hoàn toàn sự phụ thuộc vào `mockData.ts` trong `Cloud-DMS-FE/src/features/department-admin`. Mọi chỉ số lưu trữ, danh sách nhân sự và thao tác cập nhật hạn mức đều phải đọc/ghi trực tiếp qua Backend API (.NET 8 Clean Architecture).
  2. *Self-Setup State Gate (Kiểm soát trạng thái 2 thì):*
     - Trạng thái 1: Khi tài khoản `DepartmentAdmin` vừa kích hoạt và chưa có phòng ban (`user.departmentId == null`), giao diện hiển thị màn hình **Tự thiết lập Phòng ban** (`SetupDepartmentView.tsx`) để Trưởng phòng khởi tạo tên, mã phòng, thư mục gốc và trích quota cá nhân.
     - Trạng thái 2: Khi phòng ban đã được thiết lập (`user.departmentId != null`), giao diện mở khóa toàn bộ cổng quản trị [DepartmentAdminPortal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/department-admin/DepartmentAdminPortal.tsx).
  3. *Unit Standardization (Chuẩn hóa đơn vị đo lường):*
     - Backend lưu trữ và xử lý theo đơn vị số nguyên tuyệt đối: `Bytes` (kiểu `long`).
     - Frontend hiển thị thân thiện theo đơn vị: `GB` hoặc định dạng chuỗi động (`B`, `KB`, `MB`, `GB`). Toàn bộ chuyển đổi được tập trung qua helper chuẩn hóa: `1 GB = 1,073,741,824 Bytes`.
  4. *EARS Guardrails Enforcement:*
     - Frontend tự động chặn ngay trên giao diện các hành vi vi phạm: Không cho phép hạ Quota thấp hơn dung lượng thực tế nhân viên đã sử dụng (`UsedBytes`); không cho phép tăng Quota vượt quá trần dung lượng khả dụng của phòng ban (`UnallocatedDepartmentQuotaBytes`).
     - Backend vẫn đóng vai trò là chốt chặn an ninh tối cao với giao dịch `IsolationLevel.Serializable`.

---

## 2. REQ-to-Component Mapping

| Requirement (SPEC.md) | Component phụ trách | Tầng (Layer) | File path | Trách nhiệm chính |
| :--- | :--- | :--- | :--- | :--- |
| **Auth & Identity** | `AuthContext`, `LoginResponseDto` | Cross-cutting / Auth | `Cloud-DMS-FE/src/context/AuthContext.tsx`<br>`Cloud-DMS/src/CleanArchCqrs.Application/Users/DTOs/LoginResponseDto.cs` | Bổ sung `departmentId` vào `AuthUser` để Portal biết phòng ban quản lý |
| **EARS-01 .. EARS-08** (UC-DA-01) | `SetupDepartmentView.tsx`, `DepartmentSetupController` | FE View / BE API | `Cloud-DMS-FE/src/features/department-admin/views/SetupDepartmentView.tsx`<br>`Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/DepartmentSetupController.cs` | Trưởng phòng tự thiết lập tên, mã phòng, thư mục gốc và quota cá nhân |
| **EARS-23 .. EARS-24** (UC-DA-04) | `StorageOverviewView.tsx`, `DepartmentQuotasController` | FE View / BE API | `Cloud-DMS-FE/src/features/department-admin/views/StorageOverviewView.tsx`<br>`Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/DepartmentQuotasController.cs` | Hiển thị tổng hạn mức phòng ban, đã dùng, đã cấp phát và biểu đồ phân bổ |
| **EARS-25 .. EARS-26** (UC-DA-05) | `StaffManagementView.tsx`, `DepartmentEmployeesController` | FE View / BE API | `Cloud-DMS-FE/src/features/department-admin/views/StaffManagementView.tsx`<br>`Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/DepartmentEmployeesController.cs` | Xem danh sách nhân viên phân trang kèm hạn mức và dung lượng thực tế |
| **EARS-16 .. EARS-22** (UC-DA-03) | `AddMemberModal.tsx`, `DepartmentEmployeesController` | FE Component / BE API | `Cloud-DMS-FE/src/features/department-admin/components/AddMemberModal.tsx`<br>`Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/DepartmentEmployeesController.cs` | Thêm nhân viên: 100% Thư mời kích hoạt qua Email (48h), không cần nhập mật khẩu |
| **EARS-09 .. EARS-15** (UC-DA-02) | `EditQuotaModal.tsx`, `DepartmentQuotasController` | FE Component / BE API | `Cloud-DMS-FE/src/features/department-admin/components/EditQuotaModal.tsx`<br>`Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/DepartmentQuotasController.cs` | Nâng/hạ hạn mức cá nhân với kiểm tra biên an toàn (UsedBytes, Quota khả dụng) |
| **Data Orchestration** | `DepartmentContext.tsx`, `departmentApi.ts` | FE Context & Adapter | `Cloud-DMS-FE/src/features/department-admin/context/DepartmentContext.tsx`<br>`Cloud-DMS-FE/src/features/department-admin/api/departmentApi.ts` | Quản lý state tập trung, nạp dữ liệu API, reload sau mutation |

---

## 3. Data Flow Architecture

```
[DepartmentAdmin Đăng nhập]
         │
         ▼
[AuthContext: Giải mã Token / LoginResponseDto]
         │
         ├──► IF (user.departmentId == null):
         │       └── Điều hướng: <SetupDepartmentView />
         │             └── Form: Tên phòng, Mã phòng, Quota cá nhân giữ lại
         │             └── POST /api/department/setup
         │             └── Thành công: Cập nhật user.departmentId -> Chuyển sang Portal
         │
         └──► IF (user.departmentId != null):
                 └── Khởi tạo: <DepartmentProvider departmentId={user.departmentId}>
                       │
                       ├──► 1. Tải Tổng quan Dung lượng & Hạn mức Nhân viên:
                       │       └── GET /api/department/{departmentId}/quota/users
                       │       ◄── DepartmentUsersQuotaSummaryDto
                       │       └── Bind vào: <StorageOverviewView /> & Donut / Bar Charts
                       │
                       ├──► 2. Tải Danh sách Nhân viên Đầy đủ:
                       │       └── GET /api/department/{departmentId}/employees?pageNumber=1&pageSize=50
                       │       ◄── PaginatedList<UserDto>
                       │       └── Bind vào: <StaffManagementView />
                       │
                       ├──► 3. Điều chỉnh Hạn mức Cá nhân (Cấp / Thu hồi):
                       │       └── Mở <EditQuotaModal />
                       │       └── Validate Client: UsedBytes <= NewQuota <= (CurrentQuota + UnallocatedDeptQuota)
                       │       └── PUT /api/department/{departmentId}/quota/users/{userId}
                       │       └── Reload Context: Cập nhật lại Quota Summary & Danh sách
                       │
                       └──► 4. Thêm / Mời Nhân viên Mới (100% Invitation-First):
                               └── Mở <AddMemberModal />
                               └── Form: Email, Họ & Tên, SĐT, Quota ban đầu (GB) (Zero-Direct-Password)
                               └── POST /api/department/{departmentId}/employees
                               └── Reload Context & Thông báo Toast thành công
```

---

## 4. State Change Classification & Checkpoint Requirements

| Luồng / Thao tác | Phân loại State-change | Checkpoint Need | Ghi chú an ninh & toàn vẹn |
| :--- | :--- | :--- | :--- |
| **Nạp dữ liệu Quota & Employees** | `none` (Read-only) | `N/A` | Yêu cầu JWT Token hợp lệ và Policy `user.read`. |
| **Thiết lập Phòng ban (`SetupDepartment`)** | `persistence schema/business-data mutation` & `permission change` | `Architecture Review` | Tạo `Department`, tạo thư mục gốc `Folder`, gán `DepartmentId`, chuyển đổi Quota ví tạm. |
| **Thêm nhân viên mới (`CreateEmployee`)** | `business-data mutation` & `external side effect` | `Architecture Review` | Tạo `User`, gán vai trò `Employee`, tạo `UserStorageQuota`, sinh token 48h và gửi Email SMTP. |
| **Điều chỉnh Quota cá nhân (`AllocateUserQuota`)** | `business-data mutation` | `Architecture Review` | Giao dịch `Serializable`, ghi `AuditLog`, bảo vệ dữ liệu đã upload (`UsedBytes`). |

---

## 5. Shared Contract Impact

1. **Backend Authentication Contract (`LoginResponseDto`):**
   - **Tác động:** Bổ sung trường `Guid? DepartmentId = null` vào `LoginResponseDto` trong `CleanArchCqrs.Application/Users/DTOs/LoginResponseDto.cs` và gán `user.DepartmentId` tại `LoginCommandHandler.cs`.
   - **Tính tương thích:** Backward-compatible (không ảnh hưởng các trường hiện có).
   - **Lợi ích:** Frontend nhận ngay `departmentId` khi đăng nhập mà không cần parse claim từ JWT thô.
2. **Frontend API Adapter (`departmentApi.ts`):**
   - **Tác động:** Thay đổi toàn bộ các hàm gọi API sang đúng URL được bảo vệ bởi Controller trong `Cloud-DMS/src/CleanArchCqrs.API/Controllers/Department/`.
   - **Producer:** Backend `.NET 8 API`.
   - **Consumer:** Frontend `Cloud-DMS-FE/src/features/department-admin`.

---

## 6. Consistency Map (Ma trận Nhất quán Kỹ thuật)

| Requirement | Plan Component | Expected Task | Exact Verification Command | Post-code Trigger |
| :--- | :--- | :--- | :--- | :--- |
| **Contract Sync** | `LoginResponseDto`, `AuthUser` | `T001` | `dotnet build Cloud-DMS/src/CleanArchCqrs.Application` | `/sdd-trace` |
| **DTOs & API Adapter** | `departmentAdmin.dto.ts`, `departmentApi.ts` | `T002`, `T003` | `npm --prefix "Cloud-DMS-FE" run lint` | `/sdd-trace` |
| **State Context** | `DepartmentContext.tsx` | `T004` | `npm --prefix "Cloud-DMS-FE" run lint` | `/sdd-trace` |
| **UC-DA-01** (Setup Dept) | `SetupDepartmentView.tsx`, Portal Gate | `T005`, `T006` | `npm --prefix "Cloud-DMS-FE" run build` | `/sdd-trace` |
| **UC-DA-04** (Storage Overview) | `StorageOverviewView.tsx` | `T007` | `npm --prefix "Cloud-DMS-FE" run build` | `/sdd-trace` |
| **UC-DA-03, 05** (Staff List & Create) | `StaffManagementView.tsx`, `AddMemberModal.tsx` | `T008` | `npm --prefix "Cloud-DMS-FE" run build` | `/sdd-trace` |
| **UC-DA-02** (Allocate Quota) | `EditQuotaModal.tsx` | `T009` | `npm --prefix "Cloud-DMS-FE" run build` | `/sdd-trace` |
| **Cleanup & Regression** | Remove `mockData.ts`, E2E Build | `T010`, `T011` | `npm --prefix "Cloud-DMS-FE" run build` | `/git-validate` |

---

## 7. Dependencies & Implementation Order

Thứ tự triển khai tuần tự nghiêm ngặt (Strict Sequential Dependencies):
1. **Giai đoạn 1 (Foundation):** `T001` -> `T002` -> `T003` -> `T004` (Đồng bộ Auth contract, định nghĩa DTOs, cập nhật API Adapter, tạo `DepartmentContext`).
2. **Giai đoạn 2 (Self-Setup Gate):** `T005` -> `T006` (Tạo `SetupDepartmentView`, gắn vào `DepartmentAdminPortal` để giải quyết trường hợp Trưởng phòng chưa có phòng ban).
3. **Giai đoạn 3 (Core Views Data Binding):** `T007` (Bind Storage Overview) -> `T008` (Bind Staff Management & Add Member Modal).
4. **Giai đoạn 4 (Quota Mutation & Final Polish):** `T009` (Bind Edit Quota Modal) -> `T010` (Gỡ bỏ Tab Requests & xóa mockData) -> `T011` (Build Verification toàn diện).

---

## 8. Risks & Mitigations

1. **Rủi ro Không đồng nhất đơn vị dung lượng (Bytes vs GB):**
   - *Rủi ro:* Backend giao tiếp hoàn toàn bằng `Bytes` (`long`), trong khi giao diện người dùng hiển thị `GB` (`number`). Nếu tính toán sai lệch có thể dẫn tới hiển thị sai hoặc cấp vượt quota.
   - *Giải pháp:* Viết hàm chuyển đổi chuẩn hóa tập trung trong `quotaUtils.ts`:
     - `bytesToGB(bytes: number): number` (làm tròn 2 chữ số thập phân).
     - `gbToBytes(gb: number): number` (`Math.round(gb * 1024 * 1024 * 1024)`).
2. **Rủi ro Race Condition khi nhiều thao tác cấp Quota đồng thời:**
   - *Rủi ro:* Trưởng phòng mở nhiều tab hoặc điều chỉnh hạn mức liên tục có thể gây xung đột vượt trần ngân sách phòng ban.
   - *Giải pháp:* Backend đã bảo vệ toàn vẹn bằng giao dịch `IsolationLevel.Serializable`. Phía Frontend bắt lỗi `400 Bad Request`, đọc `error.response.data.detail` hoặc `message` và hiển thị Toast cảnh báo chi tiết, đồng thời tự động gọi `refreshData()` để đồng bộ lại số liệu mới nhất.
3. **Rủi ro Trưởng phòng mới chưa có `departmentId`:**
   - *Rủi ro:* Portal cố gắng gọi API `getDepartmentQuotaSummary(undefined)` gây ra lỗi `400 Bad Request` hoặc crash component.
   - *Giải pháp:* Triển khai Gate kiểm tra rõ ràng trong `DepartmentAdminPortal`: Nếu `!user?.departmentId` -> Render ngay `SetupDepartmentView`; tuyệt đối không dispatch các API đọc dữ liệu phòng ban khi `departmentId` chưa tồn tại.
4. **Rủi ro Thiếu bảng Quota Request trong CSDL:**
   - *Rủi ro:* Tab "Phê duyệt yêu cầu" (`RequestApprovalsView.tsx`) hiện đang hiển thị mock 5 yêu cầu, nhưng Backend chưa có bảng/API phiếu yêu cầu.
   - *Giải pháp:* Tạm thời chuyển tab này sang hiển thị danh sách **"Cảnh báo Hạn mức Nhân viên"** (lọc động từ các nhân viên có tỷ lệ sử dụng `>= 80%` từ API `getDepartmentUsersQuota`), kèm nút bấm nhanh "Cấp thêm Quota" để Trưởng phòng xử lý trực tiếp. Khi Backend phát triển tính năng Request Workflow sẽ tích hợp sau.

---

## 9. Questions for Human Reviewer (Đã được làm rõ)

### Q1: Hướng xử lý cho Tab "Phê duyệt yêu cầu" (`RequestApprovalsView.tsx`)
- **Vấn đề:** Backend CSDL hiện chưa có bảng/workflow Quota Request.
- **Quyết định của Người dùng:** **Loại bỏ hoàn toàn phần "Phê duyệt yêu cầu" trên FE** (`RequestApprovalsView.tsx`, `RequestDetailsModal.tsx`).
- **Trạng thái:** `RESOLVED`.
- **Triển khai:** Gỡ bỏ Tab "Phê duyệt yêu cầu" khỏi thanh Navigation của `DepartmentAdminPortal.tsx`, tập trung 2 khu vực nghiệp vụ cốt lõi: **Tổng quan dung lượng phòng ban** và **Quản lý nhân viên & Hạn mức**.

### Q2: Luồng Tạo Nhân viên Mới (`AddMemberModal.tsx`)
- **Vấn đề:** Lựa chọn giữa nhập mật khẩu trực tiếp và gửi thư mời kích hoạt qua Email.
- **Quyết định của Người dùng:** **100% Thư mời qua Email kích hoạt (Invitation-First, không nhập mật khẩu trực tiếp)**.
- **Trạng thái:** `RESOLVED`.
- **Triển khai:** Trong `AddMemberModal.tsx`, loại bỏ hoàn toàn các trường mật khẩu. Form chỉ nhận: Họ (`LastName`), Tên (`FirstName`), Email, Số điện thoại (`PhoneNumber`), và Hạn mức lưu trữ ban đầu (`StorageQuotaGB`). Hệ thống luôn tạo tài khoản ở trạng thái chờ kích hoạt (`IsActive = false`), sinh token 48 giờ và gửi email mời kích hoạt tương tự chuẩn của `create-department-admin`.

---

## 10. DoD (Definition of Done) của Pha Plan

- [x] Kế thừa đầy đủ chiều sâu `DETAILED` từ Methodology Profile trong `SPEC.md`.
- [x] 100% các Use Case (`setup-department`, `allocate-user-storage-quota`, `create-employee`, `get-department-users-storage-quota`) được ánh xạ tới component và API endpoint cụ thể.
- [x] Đã thiết lập giải pháp chuyển đổi trạng thái Self-Setup khi Trưởng phòng chưa có phòng ban.
- [x] Đã phân loại state-change, shared contract impact và ranh giới cô lập cấp phòng ban.
- [x] Bảng Consistency Map xác định rõ liên kết giữa Requirement, Plan component, command kiểm định và trigger.
- [x] Nhận diện và đề xuất giải pháp cho 4 rủi ro kỹ thuật chính.
- [x] Liệt kê 2 câu hỏi kỹ thuật cụ thể tại mục `Questions for Human reviewer`.

---

## 11. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Bản Kế hoạch Kiến trúc `PLAN.md` v1.0.0 cho tính năng bind dữ liệu thật phân hệ `DepartmentAdmin` đã được thiết lập đầy đủ, bám sát các ràng buộc Clean Architecture, EARS và Backend API hiện hữu. Kính đề nghị Human Reviewer xem xét 2 câu hỏi mở và đưa ra quyết định phê duyệt.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt kế hoạch chuyển đổi Real Data cho Department Admin theo 4 giai đoạn, loại bỏ Tab Quota Requests và áp dụng 100% Email Invitation khi tạo nhân viên.
- **Timestamp:** 2026-10-13T16:19:34+07:00
