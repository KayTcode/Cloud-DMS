# SPEC: Setup Department (Tự thiết lập Phòng ban)

- **Feature Slug:** `setup-department`
- **Module:** `DepartmentAdmin`
- **Version:** `1.5.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Cho phép Trưởng phòng tự chuyển đổi ngân sách lưu trữ được cấp tạm thời trong ví UserStorageQuota thành phòng ban chính thức, kiểm định tính hợp lệ của mã/tên phòng ban qua FluentValidation, trích hạn mức cá nhân và tự động khởi tạo thư mục gốc mặc định; bổ sung cơ chế nhận diện phòng ban đã thiết lập trong setup-info để tránh nhầm hạn mức ví cá nhân với hạn mức phòng ban; bổ sung fallback tra cứu DepartmentId trực tiếp từ CSDL nhằm đảm bảo an toàn truy cập tức thì cho Dashboard và các luồng phân quyền khi claim trong token JWT chưa kịp refresh.
  - Risk Posture: `HIGH` (Khởi tạo thực thể phòng ban, chuyển dịch quota, tạo thư mục gốc, phân quyền RBAC).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `DepartmentAdmin` (sau khi kích hoạt tài khoản theo mô hình Self-Setup) tự thiết lập phòng ban của mình:
- Cung cấp `GET /api/department/setup-info` để lấy thông tin ví Quota ban đầu do TenantAdmin đã phân bổ vào ví tạm `UserStorageQuota` (`AssignedQuotaBytes`, `AssignedQuotaGB`), loại bỏ hoàn toàn việc hardcode hạn mức ở Frontend.
- Chuyển toàn bộ vùng nhớ đang nắm giữ trong "ví tạm" (`UserStorageQuota`) thành hạn mức lưu trữ chính thức của phòng ban (`Department.StorageQuotaBytes`).
- Cho phép Trưởng phòng tự định đoạt hạn mức cá nhân giữ lại (`personalQuota`), phần còn lại dành để phân bổ cho nhân viên.
- Kiểm định schema chặt chẽ: Tên (2-100 ký tự), Mã phòng ban Regex `^[a-zA-Z0-9_-]{2,50}$`, Mô tả tối đa 500 ký tự, Quota cá nhân không âm.
- Tự động liên kết tài khoản Trưởng phòng vào phòng ban mới và cập nhật `adminQuota.DepartmentId`.
- Tự động tạo hoặc liên kết Thư mục gốc mặc định cho phòng ban (`Folder`) mà không gây xung đột Unique Index.
- Sử dụng chính sách phân quyền chuẩn xác `[Authorize(Policy = "department.manage")]` cho cả 2 endpoint.

---

## 2. Actors & Roles

- **Actor:** `DepartmentAdmin` (Người dùng có vai trò `DepartmentAdmin` và chưa gắn với phòng ban nào: `user.DepartmentId == null`, sở hữu claim `permission = department.manage`).
- **Superiors:** `TenantAdmin`, `SystemAdmin`.
- **Unauthorized:** Mọi vai trò khác hoặc Trưởng phòng đã có phòng ban bị từ chối (`400 Bad Request` / `403 Forbidden`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` DepartmentAdmin chưa có phòng ban gửi lệnh `SetupDepartmentCommand` với tên và mã phòng ban hợp lệ, `THEN` hệ thống `SHALL` tạo mới bản ghi `Department`, chuyển quota từ ví tạm (`UserStorageQuota`) sang phòng ban, gắn Trưởng phòng vào phòng ban, trích quota cá nhân, tạo thư mục gốc phòng ban (`Folder`), ghi `AuditLog` và trả về `DepartmentDto`.
- **EARS-02 (Ubiquitous):** Hệ thống `SHALL` kiểm định tính hợp lệ của dữ liệu đầu vào qua `SetupDepartmentCommandValidator`:
  - `Name` không được để trống, độ dài từ 2 đến 100 ký tự.
  - `Code` không được để trống, đúng định dạng Regex `^[a-zA-Z0-9_-]{2,50}$` (chỉ chứa chữ cái, chữ số, gạch nối và gạch dưới).
  - `Description` tối đa 500 ký tự (nếu có).
  - `AdminPersonalQuotaGB` và `AdminPersonalQuotaBytes` không được là số âm (`>= 0`).
- **EARS-03 (Optional):** `WHERE` yêu cầu có `AdminPersonalQuotaGB` hoặc `AdminPersonalQuotaBytes`, hệ thống `SHALL` cập nhật quota cá nhân của Trưởng phòng theo giá trị chỉ định.
- **EARS-04 (Optional):** `WHERE` yêu cầu không chỉ định quota cá nhân, hệ thống `SHALL` mặc định giữ lại `Min(DepartmentQuota, 1 GB)` cho Trưởng phòng.
- **EARS-05 (Unwanted):** `IF` chưa xác thực danh tính người dùng hoặc tổ chức, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Yêu cầu không hợp lệ: Chưa xác thực danh tính người dùng hoặc tổ chức."`
- **EARS-06 (Unwanted):** `IF` người gọi không có vai trò `DepartmentAdmin`, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Chỉ tài khoản Trưởng phòng (Department Admin) mới có quyền tự thiết lập phòng ban."`
- **EARS-07 (Unwanted):** `IF` tài khoản Trưởng phòng đã được gắn với một phòng ban (`user.DepartmentId.HasValue`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Tài khoản của bạn đã được gắn với một Phòng ban, không thể thiết lập thêm phòng ban mới."`
- **EARS-08 (Unwanted):** `IF` mã phòng ban (`Code`) đã tồn tại trong cùng tổ chức (`TenantId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Mã phòng ban '{Code}' đã tồn tại trong tổ chức của bạn."`
- **EARS-09 (Unwanted):** `IF` hạn mức cá nhân giữ lại là số âm hoặc vượt quá tổng dung lượng của phòng ban (`personalQuota > departmentQuotaBytes`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Hạn mức cá nhân giữ lại ({personal}) không được vượt quá tổng dung lượng của phòng ban ({total})."`
- **EARS-10 (Ubiquitous):** Hệ thống `SHALL` tự động tạo một thư mục gốc cho phòng ban với `ParentFolderId = null`, `DepartmentId = department.Id`, và `Name = department.Name`. Nếu thư mục gốc cùng tên đã tồn tại trong Tenant, hệ thống `SHALL` liên kết thư mục đó với phòng ban thay vì tạo mới để bảo đảm tính duy nhất của khóa `(TenantId, ParentFolderId, Name)`.
- **EARS-11 (Event-driven):** `WHEN` DepartmentAdmin gửi `GetDepartmentSetupInfoQuery` hoặc `SetupDepartmentCommand`, `THEN` hệ thống `SHALL` truy xuất hạn mức ví tạm từ `UserStorageQuota` (`adminQuota.QuotaBytes`); giao diện người dùng `SHALL` sử dụng hạn mức này làm giá trị trần (`max`) cho thanh trượt chọn quota cá nhân.
- **EARS-12 (Unwanted):** `IF` tài khoản Trưởng phòng chưa có ví tạm hoặc `QuotaBytes <= 0`, `THEN` hệ thống `SHALL` từ chối thao tác với thông báo: `"Tài khoản của bạn chưa được cấp hạn mức lưu trữ (Quota). Vui lòng liên hệ Quản trị viên tổ chức để được cấp dung lượng."`
- **EARS-13 (Unwanted):** `IF` xảy ra lỗi xung đột ghi CSDL (`DbUpdateException`), `THEN` hệ thống `SHALL` bắt ngoại lệ và trả về thông báo lỗi chuẩn hóa: `"Không thể lưu phòng ban. Mã phòng '{Code}' hoặc Tên phòng '{Name}' có thể đã tồn tại trong hệ thống."`

---

## 4. Non-Functional Requirements (NFR)

- **NFR-RELIABILITY-01:** Bảo toàn quota: Không làm thất thoát dung lượng đĩa khi chuyển đổi từ `UserStorageQuota` sang `Department.StorageQuotaBytes`.
- **NFR-SEC-01 (Single Department Constraint):** Mỗi tài khoản Trưởng phòng chỉ được gắn với duy nhất một phòng ban trong suốt vòng đời.
- **NFR-SEC-02 (Input Sanitization):** Tên và mã phòng ban được cắt tỉa khoảng trắng (`Trim`), mã phòng ban được viết hoa chuẩn hóa (`ToUpperInvariant`).
- **NFR-SEC-03 (RBAC Alignment):** Cả hai endpoint `setup-info` và `setup` đều sử dụng Policy `department.manage` đảm bảo Trưởng phòng có đủ quyền thực thi.

---

## 5. Data Model & Error Contract

### Cấu trúc Mã nguồn (Single Responsibility Principle)
- **Commands:**
  - [SetupDepartmentCommand.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Commands/SetupDepartment/SetupDepartmentCommand.cs): Record chứa dữ liệu yêu cầu.
  - [SetupDepartmentCommandHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Commands/SetupDepartment/SetupDepartmentCommandHandler.cs): Class xử lý nghiệp vụ thiết lập phòng ban.
  - [SetupDepartmentCommandValidator.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Commands/SetupDepartment/SetupDepartmentCommandValidator.cs): Bộ kiểm định FluentValidation.
- **Queries:**
  - [GetDepartmentSetupInfoQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Queries/GetDepartmentSetupInfo/GetDepartmentSetupInfoQuery.cs): Record truy vấn hạn mức ví tạm.
  - [GetDepartmentSetupInfoQueryHandler.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/Queries/GetDepartmentSetupInfo/GetDepartmentSetupInfoQueryHandler.cs): Class xử lý truy vấn thông tin ví tạm.
- **DTOs:**
  - [DepartmentSetupInfoDto.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Features/DepartmentAdmin/DTOs/DepartmentSetupInfoDto.cs)
  - `DepartmentDto.cs`

### HTTP Endpoints
- `GET /api/department/setup-info` (Policy: `department.manage`) -> `200 OK` (Trả về `DepartmentSetupInfoDto`)
- `POST /api/department/setup` (Policy: `department.manage`) -> `200 OK` (Trả về `DepartmentDto`) / `400 BadRequest`

| Tình huống vi phạm | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- |
| Tên/Mã sai định dạng | `400 Bad Request` | Theo FluentValidation (`Tên phòng ban phải có độ dài từ 2 đến 100 ký tự`, v.v.) |
| Quota cá nhân âm | `400 Bad Request` | `"Hạn mức cá nhân giữ lại không được là số âm."` |
| Quota cá nhân vượt trần phòng ban | `400 Bad Request` | `"Hạn mức cá nhân giữ lại (...) không được vượt quá tổng dung lượng của phòng ban (...)."` |
| Không phải DepartmentAdmin | `400 Bad Request` | `"Chỉ tài khoản Trưởng phòng (Department Admin) mới có quyền tự thiết lập phòng ban."` |
| Đã có phòng ban | `400 Bad Request` | `"Tài khoản của bạn đã được gắn với một Phòng ban, không thể thiết lập thêm phòng ban mới."` |
| Mã phòng ban trùng lặp trong Tenant | `400 Bad Request` | `"Mã phòng ban '{Code}' đã tồn tại trong tổ chức của bạn."` |
| Xung đột Unique Index CSDL | `400 Bad Request` | `"Không thể lưu phòng ban. Mã phòng '{Code}' hoặc Tên phòng '{Name}' có thể đã tồn tại trong hệ thống."` |
| Sai Token hoặc thiếu quyền | `401 Unauthorized` / `403 Forbidden` | Theo cơ chế Authorization Middleware ASP.NET Core |

---

## 6. Acceptance Criteria (BDD)

```gherkin
Scenario: Trưởng phòng tự thiết lập phòng ban thành công
  Given DepartmentAdmin có 10 GB trong ví quota tạm và chưa có phòng ban
  And người dùng sở hữu quyền "department.manage"
  When DepartmentAdmin gửi yêu cầu tạo phòng "Bơ", mã "BO", giữ lại 3 GB cho cá nhân
  Then hệ thống tạo Department "Bơ" với StorageQuotaBytes = 10 GB
  And hệ thống cập nhật UserStorageQuota của Trưởng phòng thành 3 GB
  And hệ thống gắn tài khoản Trưởng phòng vào DepartmentId mới
  And hệ thống tạo hoặc liên kết Folder gốc "Bơ"
  And ghi nhận AuditLog "SetupDepartment"
  And trả về DepartmentDto thành công

Scenario: Lấy thông tin ví quota ban đầu cho giao diện
  Given DepartmentAdmin đang chuẩn bị thiết lập phòng ban
  When Frontend gọi GET /api/department/setup-info
  Then hệ thống trả về AssignedQuotaGB = 10 GB
  And giao diện hiển thị thanh trượt với trần tối đa đúng 10 GB

Scenario: Lấy thông tin ví quota ban đầu cho giao diện khi đã hoàn tất thiết lập phòng ban
  Given DepartmentAdmin đã hoàn tất thiết lập phòng ban trước đó
  When Frontend gọi GET /api/department/setup-info
  Then hệ thống trả về HasDepartment = true kèm DepartmentId
  And hệ thống trả về AssignedQuotaGB theo dung lượng thực tế của Department (không đọc nhầm ví cá nhân)
  And Frontend tự động điều hướng vào Dashboard, không hiển thị form Setup

Scenario: Chặn mã phòng ban chứa ký tự đặc biệt không hợp lệ
  Given DepartmentAdmin gửi yêu cầu với mã "DEPT@#$%"
  Then hệ thống từ chối tại tầng FluentValidation
  And trả về lỗi: "Mã phòng ban chỉ được chứa chữ cái, chữ số, dấu gạch nối và gạch dưới, độ dài từ 2 đến 50 ký tự."

Scenario: Chặn Trưởng phòng đã có phòng ban thiết lập phòng mới
  Given Trưởng phòng đã quản lý một phòng ban
  When Trưởng phòng gửi yêu cầu thiết lập phòng ban mới
  Then hệ thống từ chối
  And trả về lỗi: "Tài khoản của bạn đã được gắn với một Phòng ban, không thể thiết lập thêm phòng ban mới."
```

---

## 7. Out of Scope

- Thiết lập phòng ban thứ hai cho cùng một Trưởng phòng.

---

## 8. Feature Lock

- **Locked Scope:** Nghiệp vụ khởi tạo phòng ban trong `SetupDepartmentCommandHandler`, `SetupDepartmentCommandValidator` và `GetDepartmentSetupInfoQueryHandler`.
- **Change Path:** `/sdd-update --feature=setup-department --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `setup-department` only.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Ghi chú:** Bản đặc tả đã được nâng cấp lên v1.5.0: Sửa triệt để lỗi Dashboard hiện số 0 và lỗi bị quay lại màn hình Setup sau khi đăng nhập lại. Cụ thể: Bổ sung fallback tra cứu CSDL cho DepartmentId và TenantId tại các Query/Command Handler phía Department Admin khi JWT claim chưa kịp refresh; nâng cấp GetDepartmentSetupInfoQueryHandler nhận diện phòng ban đã tạo để trả về dung lượng thực tế của phòng ban kèm cờ HasDepartment = true; tăng cường cơ chế đồng bộ tự động DepartmentId phía Frontend (AuthContext, DepartmentContext, SetupDepartmentView). 37/37 tests unit tests đều Passed.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả setup-department v1.5.0.
- **Timestamp:** 2026-10-14T01:30:00+07:00
