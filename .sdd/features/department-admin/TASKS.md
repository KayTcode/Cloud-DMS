# TASKS: Department Admin Real Data Binding

- **Feature Slug:** `department-admin`
- **Module:** `DepartmentAdmin`
- **Spec Reference:** `SPEC.md` (v1.0.0 — `DETAILED`)
- **Plan Reference:** `PLAN.md` (v1.0.0 — `APPROVED`)
- **Status:** `APPROVED`

---

## 1. Task List & Breakdown

### Giai đoạn 1: Nền tảng Hợp đồng & Context (Foundation & Context)

#### T001 [x] — Bổ sung DepartmentId vào Authentication Contract (Backend & Frontend)
- **Intent reference:** Intent Packet & Plan Section 1, 5
- **Input and expected outcome:**
  - *Input:* `LoginResponseDto` của Backend và `AuthUser` của Frontend chưa có trường `DepartmentId`.
  - *Outcome:* `LoginResponseDto` bổ sung `Guid? DepartmentId`; `LoginCommandHandler` gán `user.DepartmentId`; `auth.types.ts` và `AuthContext.tsx` lưu `departmentId` vào session để Portal nhận diện phòng ban của Trưởng phòng.
- **Layer and file boundary:**
  - `Cloud-DMS/src/CleanArchCqrs.Application/Users/DTOs/LoginResponseDto.cs`
  - `Cloud-DMS/src/CleanArchCqrs.Application/Users/Commands/Login/LoginCommandHandler.cs`
  - `Cloud-DMS-FE/src/types/auth.types.ts`
  - `Cloud-DMS-FE/src/context/AuthContext.tsx`
- **Owner and dependencies:** AI Agent / Fullstack Engineer; `blockedBy`: none
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet build "Cloud-DMS/src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj"
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `shared/public contract`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Bổ sung trường tùy chọn trong Authentication Contract; backward-compatible.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T002 [x] — Định nghĩa DTOs Chuẩn hóa cho Department Admin
- **Intent reference:** `SPEC.md` Section 5.2 & Plan Section 2
- **Input and expected outcome:**
  - *Input:* `department.types.ts` hiện tại chỉ chứa các kiểu dữ liệu phục vụ mock data.
  - *Outcome:* Tạo file `departmentAdmin.dto.ts` chứa đầy đủ các interface khớp 100% với Backend DTO: `DepartmentUsersQuotaSummaryDto`, `UserStorageQuotaDto`, `CreateEmployeePayload`, `AllocateUserQuotaPayload`, `SetupDepartmentPayload`, `DepartmentDto`.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/types/departmentAdmin.dto.ts`
  - `Cloud-DMS-FE/src/features/department-admin/types/index.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T001`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Đồng bộ kiểu dữ liệu với Backend Application DTOs.
- **High-risk review route:** `N/A`
- **Post-code review:** `N/A`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T003 [x] — Cập nhật API Adapter (departmentApi.ts) gọi Backend Controllers
- **Intent reference:** `SPEC.md` Section 5.3 & Plan Section 2
- **Input and expected outcome:**
  - *Input:* `departmentApi.ts` đang gọi các endpoint giả lập (`/api/departments/current/...`).
  - *Outcome:* Viết lại `departmentApi.ts` gọi chính xác các endpoint của Backend:
    - `getDepartmentQuotaSummary(deptId: string)` -> `GET /api/department/{deptId}/quota/users`
    - `getDepartmentEmployees(deptId: string, page, size, search)` -> `GET /api/department/{deptId}/employees`
    - `allocateUserQuota(deptId: string, userId: string, quotaBytes: number)` -> `PUT /api/department/{deptId}/quota/users/{userId}`
    - `createEmployee(deptId: string, payload)` -> `POST /api/department/{deptId}/employees`
    - `setupDepartment(payload)` -> `POST /api/department/setup`
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/api/departmentApi.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T002`
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `shared/public contract`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Kết nối trực tiếp với Clean Architecture API Controllers.
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T004 [x] — Xây dựng DepartmentContext.tsx Quản lý State Tập trung
- **Intent reference:** Plan Section 1, 3 (Data Flow Architecture)
- **Input and expected outcome:**
  - *Input:* Các view của Department Admin đang quản lý state rời rạc trong `DepartmentAdminPortal.tsx` bằng `useState(mockData)`.
  - *Outcome:* Tạo `DepartmentContext.tsx` và `useDepartment()` hook:
    - Quản lý trạng thái: `departmentSummary`, `members`, `isLoading`, `error`, `hasDepartment`.
    - Cung cấp các hành động: `refreshData()`, `allocateQuota()`, `createEmployee()`, `setupDepartment()`.
    - Tự động nạp dữ liệu khi Trưởng phòng đã có `departmentId`.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/context/DepartmentContext.tsx`
  - `Cloud-DMS-FE/src/features/department-admin/index.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T003`
- **Estimated effort:** 1 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Quản lý state tập trung cho toàn phân hệ Department Admin.
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 2: Luồng Tự thiết lập Phòng ban (Self-Setup Flow)

#### T005 [x] — Xây dựng Giao diện Tự thiết lập Phòng ban (SetupDepartmentView.tsx)
- **Intent reference:** `SPEC.md` UC-DA-01 (`setup-department`) & `EARS-01` .. `EARS-08`
- **Input and expected outcome:**
  - *Input:* Chưa có giao diện để Trưởng phòng tự khởi tạo phòng ban khi `departmentId == null`.
  - *Outcome:* Tạo `SetupDepartmentView.tsx`:
    - Form trực quan: Tên phòng ban (`Name`), Mã phòng ban (`Code`), Mô tả (`Description`), Hạn mức cá nhân giữ lại (`AdminPersonalQuotaGB`).
    - Nút submit gọi `setupDepartment()` qua `useDepartment()`.
    - Thông báo lỗi trùng mã phòng hoặc hạn mức không hợp lệ từ Backend.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/views/SetupDepartmentView.tsx`
  - `Cloud-DMS-FE/src/features/department-admin/views/index.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T004`
- **Estimated effort:** 1 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `business-data mutation`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Thực thi hợp đồng `POST /api/department/setup`.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T006 [x] — Tích hợp State Gate vào Portal & Loại bỏ Tab "Phê duyệt yêu cầu"
- **Intent reference:** Plan Section 1, 9 (Q1 Resolved)
- **Input and expected outcome:**
  - *Input:* `DepartmentAdminPortal.tsx` đang có Tab "Phê duyệt yêu cầu" (dùng mock data) và không kiểm tra `departmentId`.
  - *Outcome:*
    - Kiểm tra điều kiện: Nếu `!user?.departmentId` -> hiển thị `<SetupDepartmentView />`.
    - Nếu đã có `departmentId` -> bọc `<DepartmentProvider>` và hiển thị cổng chính.
    - Loại bỏ hoàn toàn Tab "Phê duyệt yêu cầu" khỏi thanh Navigation, chỉ giữ 2 tab: "Tổng quan dung lượng" và "Quản lý nhân viên".
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/DepartmentAdminPortal.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T005`
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `no impact`
- **High-risk review route:** `N/A`
- **Post-code review:** `N/A`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 3: Bind Dữ liệu Thật cho Storage Overview & Staff Management

#### T007 [x] — Bind Dữ liệu Thật vào StorageOverviewView.tsx & Tiện ích Quota Utils
- **Intent reference:** `SPEC.md` UC-DA-04 (`get-department-users-storage-quota`) & `EARS-23`, `EARS-24`
- **Input and expected outcome:**
  - *Input:* `StorageOverviewView.tsx` đang đọc số liệu từ `mockData.ts`.
  - *Outcome:*
    - Tạo `quotaUtils.ts` chuẩn hóa chuyển đổi `bytesToGB`, `formatBytes`, `gbToBytes`.
    - Bind dữ liệu thật từ `DepartmentContext`: Tổng quota phòng ban, Đã sử dụng, Đã phân bổ cho nhân viên, Khả dụng chưa cấp.
    - Cập nhật Donut Chart với phần trăm thực tế (`(departmentUsedBytes / departmentQuotaBytes) * 100`).
    - Tạo cảnh báo động: Lọc danh sách nhân viên có mức dùng `>= 80%` làm danh sách cảnh báo thời gian thực.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/utils/quotaUtils.ts`
  - `Cloud-DMS-FE/src/features/department-admin/views/StorageOverviewView.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T006`
- **Estimated effort:** 1.5 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Hiển thị dữ liệu từ `DepartmentUsersQuotaSummaryDto`.
- **High-risk review route:** `N/A`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T008 [x] — Bind Dữ liệu Thật vào StaffManagementView.tsx & Cập nhật AddMemberModal.tsx (100% Email Invitation)
- **Intent reference:** `SPEC.md` UC-DA-03, UC-DA-05 (`create-employee`, `get-department-employees`) & `EARS-16` .. `EARS-22`, Plan Section 9 (Q2 Resolved)
- **Input and expected outcome:**
  - *Input:* `StaffManagementView.tsx` hiển thị 8 nhân viên giả; `AddMemberModal.tsx` nhận các trường không khớp với Backend và không gọi API.
  - *Outcome:*
    - Bind danh sách nhân viên thật từ `useDepartment()` (tên, email, quota được cấp, dung lượng đã dùng, trạng thái kích hoạt).
    - Cập nhật `AddMemberModal.tsx`:
      - Loại bỏ toàn bộ trường mật khẩu (100% Invitation-First / Zero-Direct-Password).
      - Nhận: Họ (`LastName`), Tên (`FirstName`), Email, Số điện thoại (`PhoneNumber`), Hạn mức ban đầu (`StorageQuotaGB`).
      - Gọi `createEmployee()` qua Context, hiển thị Toast thông báo đã gửi thư mời kích hoạt qua Email (thời hạn 48 giờ) và reload danh sách.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/views/StaffManagementView.tsx`
  - `Cloud-DMS-FE/src/features/department-admin/components/AddMemberModal.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T007`
- **Estimated effort:** 1.5 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `business-data mutation & external side effect`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Thực thi hợp đồng `POST /api/department/{id}/employees`.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

### Giai đoạn 4: Điều chỉnh Quota, Dọn dẹp Mock Data & E2E Build

#### T009 [x] — Cập nhật EditQuotaModal.tsx Áp dụng Ràng buộc EARS và Gọi API Thật
- **Intent reference:** `SPEC.md` UC-DA-02 (`allocate-user-storage-quota`) & `EARS-09` .. `EARS-15`
- **Input and expected outcome:**
  - *Input:* `EditQuotaModal.tsx` chỉ cập nhật state bộ nhớ cục bộ, không gọi API.
  - *Outcome:*
    - Thanh trượt và ô nhập số bị khóa chặn biên:
      - Giá trị tối thiểu = `Math.ceil(member.usedBytes / 1024^3)` (không cho hạ thấp hơn dung lượng đã tải lên).
      - Giá trị tối đa = `currentQuotaGB + unallocatedDeptQuotaGB` (không cho vượt trần phòng ban).
    - Nút lưu gọi `allocateQuota(member.userId, newQuotaBytes)` qua Context.
    - Bắt lỗi giao dịch từ Backend nếu có xung đột và hiển thị thông báo chi tiết.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/components/EditQuotaModal.tsx`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T008`
- **Estimated effort:** 1 giờ
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `business-data mutation`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** Thực thi hợp đồng `PUT /api/department/{id}/quota/users/{userId}`.
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T010 [x] — Dọn dẹp Mock Data & Gỡ bỏ Component Request Thừa
- **Intent reference:** Zero-Mock Policy & Cleanup
- **Input and expected outcome:**
  - *Input:* Còn tồn tại `mockData.ts`, `RequestApprovalsView.tsx`, `RequestDetailsModal.tsx` không còn sử dụng.
  - *Outcome:* Xóa bỏ hoặc decommission các file và component này; dọn dẹp các import thừa trong `components/index.ts` và `views/index.ts` để tối ưu kích thước bundle.
- **Layer and file boundary:**
  - `Cloud-DMS-FE/src/features/department-admin/data/mockData.ts`
  - `Cloud-DMS-FE/src/features/department-admin/views/RequestApprovalsView.tsx`
  - `Cloud-DMS-FE/src/features/department-admin/components/RequestDetailsModal.tsx`
  - `Cloud-DMS-FE/src/features/department-admin/components/index.ts`
  - `Cloud-DMS-FE/src/features/department-admin/views/index.ts`
- **Owner and dependencies:** AI Agent / Frontend Engineer; `blockedBy`: `T009`
- **Estimated effort:** 30 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  npm --prefix "Cloud-DMS-FE" run lint
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `no impact`
- **High-risk review route:** `N/A`
- **Post-code review:** `N/A`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

#### T011 [x] — Kiểm thử End-to-End Build Toàn diện & Regression Verification
- **Intent reference:** Definition of Done & Quality Gate
- **Input and expected outcome:**
  - *Input:* Toàn bộ mã nguồn Backend và Frontend đã được sửa đổi và tích hợp.
  - *Outcome:*
    - Backend build và test suite: 0 Warning, 0 Error, 100% tests Passed.
    - Frontend build: `tsc --noEmit` Passed và `vite build` thành công 0 lỗi.
- **Layer and file boundary:** Toàn bộ solution `Cloud-DMS` và project `Cloud-DMS-FE`.
- **Owner and dependencies:** AI Agent / Tech Lead; `blockedBy`: `T010`
- **Estimated effort:** 45 phút
- **Sizing signal:** `within-guideline`
- **Profile binding and exact verification command:**
  ```powershell
  dotnet test "Cloud-DMS/tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj"
  npm --prefix "Cloud-DMS-FE" run build
  ```
- **Scope category:** `none`
- **Human checkpoint:** `N/A`
- **Shared contract and sync-back:** `/sdd-trace` và `/git-validate`
- **High-risk review route:** `Architecture & Security Review Mandatory`
- **Post-code review:** `required`
- **Execution readiness:** `single-owned; direct; exclusive boundary`

---

## 2. DoD (Definition of Done)

- [x] 100% các task đều có ranh giới file, layer, owner và verification command chính xác.
- [x] Mỗi task độc lập hoặc khai báo rõ `blockedBy`.
- [x] Đã phân rã tuần tự 4 giai đoạn logic từ Foundation -> Self-Setup -> Views Binding -> Quota Mutation & Cleanup.
- [x] Sizing tín hiệu toàn bộ nằm trong định mức (`within-guideline`).
- [x] Phạm vi task bám sát 100% `SPEC.md` v1.0.0 và `PLAN.md` v1.0.0 (`APPROVED`).
- [x] Đã thiết lập đầy đủ bài kiểm thử typecheck, unit test và build verification cho cả hai nền tảng.

---

## 3. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Toàn bộ 11 tasks (T001–T011) thuộc cả 4 giai đoạn đã được thực thi và kiểm định thành công (Backend 26/26 tests Passed; Frontend Lint & Build Passed 100%). Kính đề nghị Human Reviewer nghiệm thu và phê duyệt hoàn thành.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Đồng ý với bảng nhiệm vụ TASKS.md v1.0.0 (11 tasks), tiến hành thực thi theo lộ trình 4 giai đoạn.
- **Timestamp:** 2026-10-13T16:26:27+07:00

---

## 4. Execution Record & Verification Summary

### Execution Details:
- **Execution Mode:** `direct`
- **Tasks Executed:** T001, T002, T003, T004, T005, T006, T007, T008, T009, T010, T011 (11/11 Tasks COMPLETED)

| Task | Boundary | Status | Verification Command | Result |
| :--- | :--- | :--- | :--- | :--- |
| **T001** | `LoginResponseDto.cs`, `LoginCommandHandler.cs`, `auth.types.ts`, `AuthContext.tsx` | `COMPLETED` | `dotnet build` & `npm run lint` | `0 Warning, 0 Error` |
| **T002** | `departmentAdmin.dto.ts`, `types/index.ts` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T003** | `departmentApi.ts`, `endpoints.ts` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T004** | `DepartmentContext.tsx`, `index.ts` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T005** | `SetupDepartmentView.tsx`, `views/index.ts` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T006** | `DepartmentAdminPortal.tsx` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T007** | `quotaUtils.ts`, `StorageOverviewView.tsx` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T008** | `StaffManagementView.tsx`, `AddMemberModal.tsx` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T009** | `EditQuotaModal.tsx` | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T010** | Cleanup Mock Data & Request Tab | `COMPLETED` | `npm run lint` | `Passed (0 error)` |
| **T011** | Backend Regression & Frontend Production Build | `COMPLETED` | `dotnet test` & `npm run build` | `Backend 26/26 Passed; Frontend Vite Built in 1.43s` |

