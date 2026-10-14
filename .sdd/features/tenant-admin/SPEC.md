# TenantAdmin Specification (SPEC.md)

- **Feature Slug:** `tenant-admin`
- **Version:** `1.1.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Phân hệ quản trị cấp tổ chức (`TenantAdmin`) sở hữu quyền điều hành cơ cấu tổ chức, bổ nhiệm lãnh đạo phòng ban (`DepartmentAdmin` theo mô hình 100% Email Invitation và tự động phân bổ Quota khả dụng khi Self-Setup) và điều phối tài nguyên lưu trữ nội bộ (`StorageQuota`) trong môi trường Multi-Tenancy. Đòi hỏi tính toàn vẹn dữ liệu giao dịch (`Serializable`), chống Race Condition khi cấp phát quota và ngăn chặn rò rỉ dữ liệu chéo tổ chức (IDOR).
  - Risk Posture: `HIGH` (Quản lý hạn mức lưu trữ phòng ban, phân quyền nhân sự, cô lập dữ liệu tổ chức).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

### 1.1 Bối cảnh (Context)
Trong hệ thống Cloud-DMS (Cloud Document Management System), phân hệ `TenantAdmin` (Quản trị viên tổ chức/công ty) đóng vai trò là tầng điều phối tài nguyên và nhân sự cấp trung gian:
- Nằm dưới quyền khởi tạo và phân bổ của `SystemAdmin` (Super Admin).
- Đứng trên các `DepartmentAdmin` (Trưởng phòng) và `Employee` (Nhân viên) trong cấu trúc phân cấp tổ chức.
- Có thẩm quyền tuyệt đối trong phạm vi `TenantId` của mình:
  1. Bổ nhiệm Trưởng phòng (`DepartmentAdmin`), hỗ trợ cả gán trực tiếp vào phòng ban có sẵn lẫn mô hình "Admin-First / Self-Setup" kèm cơ chế thư mời qua Email.
  2. Điều phối, tăng/giảm hạn mức dung lượng lưu trữ (`Department StorageQuotaBytes`) từ quỹ tổng của Tenant xuống các phòng ban.
  3. Truy vấn, thống kê cấu trúc phòng ban, nhân sự và mức độ sử dụng dung lượng đĩa của toàn tổ chức.
  4. Tiếp nhận lời mời và kích hoạt tài khoản Quản trị tổ chức (`AcceptInvitation`) từ SystemAdmin.

### 1.2 Mục tiêu (Goal)
- Đảm bảo tính toàn vẹn và an toàn cho hạn mức lưu trữ: Áp dụng cơ chế kiểm tra 3 lớp (3-tier safety checks) nhằm ngăn chặn việc hạ quota xuống thấp hơn dung lượng đã sử dụng hoặc thấp hơn tổng quota đã chia cho nhân viên, đồng thời không bao giờ vượt quá dung lượng còn trống của Tenant.
- Đảm bảo tính nhất quán dữ liệu cao (High Consistency) khi phân bổ quota đồng thời thông qua giao dịch CSDL mức cô lập `Serializable`.
- Đảm bảo an toàn phân quyền Multi-Tenancy: Tuyệt đối cô lập dữ liệu giữa các tổ chức; ngăn chặn mọi hành vi truy cập hoặc sửa đổi tài nguyên của Tenant khác (chống IDOR).
- Tối ưu hóa trải nghiệm khởi tạo nhân sự với quy trình mời qua Email và token một lần hết hạn trong 48 giờ.

---

## 2. Actors & Roles

| Actor / Role | Quyền hạn (Permissions) | Mô tả trách nhiệm |
| :--- | :--- | :--- |
| **TenantAdmin** | `user.create`, `department.read`, `storage.manage`, `files.read` (trong phạm vi Tenant) | Quản trị viên của một Tổ chức cụ thể. Thao tác trên phòng ban, bổ nhiệm Trưởng phòng, điều phối quota phòng ban thuộc tổ chức mình. |
| **SystemAdmin** | Toàn quyền hệ thống (`SystemAdmin.*`) | Khởi tạo TenantAdmin và cấp phát quota tổng cho Tenant từ Storage Provider. |
| **DepartmentAdmin** | Quản trị phòng ban (`department.manage`, `user.manage.dept`) | Tiếp nhận vai trò từ TenantAdmin, thiết lập phòng ban và quản lý nhân viên dưới quyền. |
| **Unauthorized / Other Tenant** | Bị từ chối truy cập (`403 Forbidden` / `401 Unauthorized`) | Không được phép can thiệp hoặc truy cập dữ liệu của Tenant khác. |

---

## 3. Functional Requirements (EARS)

Phân hệ `TenantAdmin` bao gồm các Use Case nghiệp vụ chính được chuẩn hóa theo 5 mẫu cú pháp EARS (`Ubiquitous`, `Event-driven`, `State-driven`, `Optional`, `Unwanted`).

### 3.1 Bổ nhiệm Trưởng phòng (CreateDepartmentAdmin)

#### UC-TA-01: Bổ nhiệm Trưởng phòng ban mới
- **EARS-01 (Event-driven):** `WHEN` TenantAdmin gửi yêu cầu bổ nhiệm Trưởng phòng với email hợp lệ, chưa tồn tại trong danh sách tài khoản đang hoạt động (`IsActive = true`) và `TenantId` hợp lệ, `THEN` hệ thống `SHALL` tạo mới bản ghi `User` với `IsActive = false`, `EmailConfirmed = false`, sinh mật khẩu ngẫu nhiên tạm thời (GUID) được băm an toàn bằng BCrypt, gán vai trò `DepartmentAdmin`, khởi tạo `UserStorageQuota`, sinh `EmailVerificationToken` (loại `DepartmentAdminInvitation`, hết hạn trong 48 giờ), gửi email mời kích hoạt qua `IEmailService`, ghi `AuditLog` và trả về `UserDto`.
- **EARS-01A (Event-driven / Re-creation):** `WHERE` email người dùng đã tồn tại trong hệ thống nhưng tài khoản cũ đó đã bị xóa mềm (`IsActive = false`), `THEN` hệ thống `SHALL` cho phép tạo lại tài khoản với email này (bằng cách giải phóng email của bản ghi cũ sang dạng `{email}__deleted_{timestamp}` để tránh vi phạm Unique Index), khởi tạo tài khoản mới ở trạng thái `IsActive = false`, gán mật khẩu ngẫu nhiên tạm thời, vai trò `DepartmentAdmin`, cấp `EmailVerificationToken` 48 giờ và gửi email mời kích hoạt mà không báo lỗi trùng email.
- **EARS-02 (Optional):** `WHERE` yêu cầu chỉ định `DepartmentId` hợp lệ thuộc Tenant, hệ thống `SHALL` liên kết Trưởng phòng trực tiếp với phòng ban đó và kiểm tra phòng ban chưa có Trưởng phòng nào khác.
- **EARS-03 (Optional):** `WHERE` yêu cầu không chỉ định `DepartmentId` (mô hình Self-Setup), hệ thống `SHALL` cho phép `DepartmentId = null`, cấp `StorageQuotaBytes` vào bản ghi `UserStorageQuota` của Trưởng phòng trích từ dung lượng khả dụng của Tenant (`tenant.StorageQuotaBytes - (existingDeptsQuota + pendingAdminsQuota)`). Nếu request không chỉ định quota cụ thể hoặc `<= 0`, hệ thống `SHALL` tự động cấp toàn bộ dung lượng khả dụng này vào ví tạm của Trưởng phòng, không được gán mặc định bằng 0.
- **EARS-04 (Mandatory Invitation / Passwordless Input):** Hệ thống `SHALL` loại bỏ việc nhập mật khẩu thủ công từ phía TenantAdmin (100% Invitation-First); mọi tài khoản Trưởng phòng tạo mới đều bắt buộc trải qua luồng xác thực và kích hoạt email (`AcceptInvitation`) trước khi có thể đăng nhập.
- **EARS-05 (Unwanted / Duplicate Active Email):** `IF` email người dùng đã tồn tại trong CSDL và tài khoản đó đang hoạt động (`IsActive = true`) (không phân biệt chữ hoa/thường), `THEN` hệ thống `SHALL` từ chối thao tác và trả về lỗi thông báo trùng lặp email.
- **EARS-06 (Unwanted):** `IF` `TenantId` không tồn tại hoặc tổ chức đang ở trạng thái bị vô hiệu hóa (`IsActive = false`), `THEN` hệ thống `SHALL` từ chối thao tác và trả về thông báo lỗi tổ chức không hợp lệ.
- **EARS-07 (Unwanted):** `IF` `DepartmentId` được chỉ định không thuộc về `TenantId` hoặc phòng ban đang bị vô hiệu hóa, `THEN` hệ thống `SHALL` từ chối thao tác và trả về thông báo lỗi phòng ban không tồn tại trong tổ chức.
- **EARS-08 (Unwanted):** `IF` phòng ban được chỉ định đã có một Trưởng phòng (`Role = "DepartmentAdmin"`) đang quản lý, `THEN` hệ thống `SHALL` từ chối thao tác với thông báo mỗi phòng ban chỉ được bổ nhiệm duy nhất một Trưởng phòng.
- **EARS-09 (Unwanted):** `IF` hạn mức cá nhân yêu cầu cho Trưởng phòng (khi có phòng ban) vượt quá dung lượng khả dụng của chính phòng ban đó (`department.StorageQuotaBytes - usersAllocatedInDept`), `THEN` hệ thống `SHALL` từ chối thao tác và thông báo vượt hạn mức phòng ban.
- **EARS-10 (Unwanted):** `IF` hạn mức cấp trước (khi Self-Setup) vượt quá dung lượng khả dụng của Tenant (`tenant.StorageQuotaBytes - (existingDeptsQuota + pendingAdminsQuota)`), `THEN` hệ thống `SHALL` từ chối thao tác và thông báo vượt hạn mức khả dụng của tổ chức.
- **EARS-11 (Ubiquitous):** Hệ thống `SHALL` tự động ghi nhận bản ghi `AuditLog` với hành động `InviteDepartmentAdminWithQuota` lưu vết người thực hiện, thời gian và hạn mức đã cấp.

---

### 3.2 Phân bổ và Điều chỉnh Hạn mức Lưu trữ Phòng ban (AllocateDepartmentStorageQuota)

#### UC-TA-02: Cấp phát và Điều chỉnh Hạn mức Lưu trữ Phòng ban
- **EARS-13 (Event-driven):** `WHEN` TenantAdmin gửi yêu cầu điều chỉnh hạn mức (`QuotaBytes`) cho một phòng ban hợp lệ thuộc Tenant, `THEN` hệ thống `SHALL` mở giao dịch CSDL với mức cô lập `IsolationLevel.Serializable`, kiểm tra 3 lớp an toàn, cập nhật `department.StorageQuotaBytes`, ghi `AuditLog` và trả về `DepartmentStorageQuotaDto`.
- **EARS-14 (Unwanted):** `IF` hạn mức yêu cầu `QuotaBytes < 0`, `THEN` hệ thống `SHALL` từ chối thao tác và trả về lỗi hạn mức không được là số âm.
- **EARS-15 (Unwanted):** `IF` `TenantId` hoặc `DepartmentId` không tồn tại, hoặc phòng ban không thuộc về Tenant (`department.TenantId != request.TenantId`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi không tìm thấy phòng ban trong tổ chức.
- **EARS-16 (Unwanted):** `IF` hạn mức mới `QuotaBytes` nhỏ hơn dung lượng thực tế mà phòng ban đã sử dụng (`department.StorageUsedBytes`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi không thể hạ hạn mức thấp hơn dung lượng phòng ban đã sử dụng (Lớp bảo vệ 1).
- **EARS-17 (Unwanted):** `IF` hạn mức mới `QuotaBytes` nhỏ hơn tổng hạn mức mà Trưởng phòng đã phân bổ cho các thành viên trong phòng (`totalAllocatedToUsers`), `THEN` hệ thống `SHALL` hủy transaction và trả về thông báo lỗi yêu cầu thu hồi bớt hạn mức thành viên trước khi hạ quota phòng (Lớp bảo vệ 2).
- **EARS-18 (Unwanted):** `IF` hạn mức mới `QuotaBytes` vượt quá dung lượng khả dụng chưa phân bổ của Tenant (`availableInTenant = tenant.StorageQuotaBytes - otherDepartmentsQuota`), `THEN` hệ thống `SHALL` hủy transaction và trả về thông báo lỗi vượt quá hạn mức lưu trữ khả dụng của tổ chức (Lớp bảo vệ 3).
- **EARS-19 (Ubiquitous):** Hệ thống `SHALL` ghi lại nhật ký kiểm toán `AuditLog` với hành động `AllocateDepartmentQuota`, ghi nhận chi tiết giá trị hạn mức cũ và mới cùng định dạng dung lượng thân thiện với người dùng (`FormatBytes`).

---

### 3.3 Truy vấn Danh sách Phòng ban Tổ chức (GetDepartments)

#### UC-TA-03: Lấy Danh sách Phòng ban và Thống kê Lưu trữ
- **EARS-20 (Event-driven):** `WHEN` TenantAdmin gửi yêu cầu xem danh sách các phòng ban thuộc tổ chức theo `TenantId`, `THEN` hệ thống `SHALL` truy vấn toàn bộ các phòng ban thuộc Tenant, nạp thông tin thành viên và vai trò, tính toán số lượng nhân sự (`HeadCount`), xác định Trưởng phòng (`Lead`, `AdminEmail`), tính toán quy đổi dung lượng GB và trả về danh sách `List<DepartmentDto>`.
- **EARS-21 (State-driven):** `WHILE` thực thi truy vấn danh sách phòng ban, hệ thống `SHALL` sử dụng cơ chế `AsNoTracking()` và `AsSplitQuery()` để tối ưu hóa hiệu năng đọc dữ liệu và chống nghẽn bộ nhớ.
- **EARS-22 (Ubiquitous):** Danh sách phòng ban trả về `SHALL` luôn được sắp xếp theo thứ tự bảng chữ cái tên phòng ban (`OrderBy(d => d.Name)`).

---

### 3.4 Kích hoạt Tài khoản và Tiếp nhận Quản trị (AcceptInvitation)

#### UC-TA-04: Tiếp nhận Lời mời Quản trị viên Tổ chức
- **EARS-23 (Event-driven):** `WHEN` TenantAdmin truy cập liên kết lời mời từ email và gửi mật khẩu mới kèm token xác thực hợp lệ, `THEN` hệ thống `SHALL` kiểm tra tính hợp lệ của token, cập nhật mật khẩu băm, chuyển trạng thái `IsActive = true`, đánh dấu token đã sử dụng (`IsUsed = true`), tạo `UserStorageQuota` mặc định nếu chưa có, sinh mã JWT Access Token và Refresh Token cho người dùng đăng nhập ngay.
- **EARS-24 (Unwanted):** `IF` token không tồn tại, đã được sử dụng hoặc đã hết hạn (quá 48 giờ), `THEN` hệ thống `SHALL` từ chối kích hoạt và trả về thông báo lỗi tương ứng.

---

### 3.5 Ranh giới Cô lập Đa tổ chức (Multi-Tenant Isolation)

- **EARS-25 (Ubiquitous):** Hệ thống `SHALL` luôn trích xuất hoặc đối chiếu `TenantId` từ Claims của JWT Access Token của người dùng đang đăng nhập (`ICurrentUserService`) đối với mọi yêu cầu thao tác.
- **EARS-26 (Unwanted):** `IF` người dùng có vai trò `TenantAdmin` gửi yêu cầu can thiệp tới `TenantId`, `DepartmentId`, hoặc `UserId` thuộc về một tổ chức khác, `THEN` hệ thống `SHALL` từ chối thực thi và trả về lỗi truy cập trái phép (`403 Forbidden` hoặc `400 Bad Request`).

---

## 4. Non-Functional Requirements (NFR)

1. **NFR-SEC-01 (Cô lập Dữ liệu Đa tổ chức - Multi-Tenant Isolation):** Toàn bộ dữ liệu phòng ban, người dùng, quota và nhật ký kiểm toán phải được gắn chặt với `TenantId`. Không một truy vấn nào được phép rò rỉ dữ liệu của Tenant khác (chống IDOR 100%).
2. **NFR-SEC-02 (Bảo mật Mời qua Email):** Token mời (`EmailVerificationToken`) phải được sinh ngẫu nhiên với độ entropy cao (chuỗi GUID kép), có thời hạn sống tối đa **48 giờ**, chỉ dùng được duy nhất một lần (`IsUsed = true`), và tự động vô hiệu hóa sau khi kích hoạt thành công.
3. **NFR-RELIABILITY-01 (Toàn vẹn Hạn mức Phân cấp):** Tổng hạn mức cấp phát cho các phòng ban (`Sum(Department.StorageQuotaBytes)`) cộng với hạn mức tạm của các Trưởng phòng chờ thiết lập (`Sum(PendingAdminsQuota)`) không bao giờ được vượt quá `Tenant.StorageQuotaBytes` tại bất kỳ thời điểm nào.
4. **NFR-RELIABILITY-02 (Chống Race Condition):** Mọi thao tác điều chỉnh hạn mức lưu trữ phòng ban phải được thực thi trong transaction CSDL với mức cô lập `IsolationLevel.Serializable` để bảo đảm không xảy ra tranh chấp dữ liệu khi nhiều quản trị viên cùng thao tác.
5. **NFR-PERF-01 (Thời gian Phản hồi Truy vấn):** Endpoint `GetDepartments` phải phản hồi trong thời gian **< 200ms** đối với tổ chức có tối đa 100 phòng ban và 5,000 nhân sự (sử dụng tối ưu hóa `AsNoTracking` và `AsSplitQuery`).
6. **NFR-AUDIT-01 (Lưu vết Kiểm toán):** 100% các thao tác thay đổi hạn mức phòng ban hoặc bổ nhiệm nhân sự Trưởng phòng phải ghi lại bản ghi `AuditLog` chi tiết gồm `UserId`, `TenantId`, `Action`, `Details`, và `CreatedAt`.

---

## 5. Data Model & Contracts

### 5.1 Các thực thể chính liên quan (Domain Entities)

- `Tenant`: Đại diện cho tổ chức (`Id`, `Name`, `Code`, `StorageQuotaBytes`, `StorageUsedBytes`, `IsActive`).
- `Department`: Phòng ban thuộc tổ chức (`Id`, `TenantId`, `Name`, `Code`, `Description`, `StorageQuotaBytes`, `StorageUsedBytes`, `IsActive`).
- `User`: Tài khoản người dùng (`Id`, `TenantId`, `DepartmentId`, `Email`, `PasswordHash`, `FirstName`, `LastName`, `PhoneNumber`, `IsActive`, `EmailConfirmed`).
- `UserRole`: Liên kết N-N giữa `User` và `Role` (`UserId`, `RoleId`).
- `UserStorageQuota`: Quản lý hạn mức cá nhân hoặc "ví tạm giữ quota" cho Trưởng phòng (`UserId`, `QuotaBytes`, `UsedBytes`).
- `EmailVerificationToken`: Token kích hoạt tài khoản gửi qua email (`Token`, `UserId`, `TokenType`, `ExpiresAt`, `IsUsed`).
- `AuditLog`: Bản ghi kiểm toán hành vi quản trị (`TenantId`, `UserId`, `Action`, `EntityName`, `EntityId`, `Details`, `CreatedAt`).

### 5.2 Data Transfer Objects (DTOs)

#### `DepartmentDto`
```csharp
public class DepartmentDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int HeadCount { get; set; }
    public string Lead { get; set; }
    public string? AdminEmail { get; set; }
    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public double AllocatedStorageGB => ...;
    public double UsedStorageGB => ...;
    public DateTime CreatedAt { get; set; }
}
```

#### `DepartmentStorageQuotaDto`
```csharp
public class DepartmentStorageQuotaDto
{
    public Guid DepartmentId { get; set; }
    public Guid TenantId { get; set; }
    public string DepartmentName { get; set; }
    public string DepartmentCode { get; set; }
    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public long AvailableBytes => Math.Max(0, StorageQuotaBytes - StorageUsedBytes);
    public double UsagePercentage => ...;
    public long TotalAllocatedToUsersBytes { get; set; }
    public long UnallocatedBytes => Math.Max(0, StorageQuotaBytes - TotalAllocatedToUsersBytes);
}
```

### 5.3 Error Contracts

| Tình huống vi phạm | Kiểu kết quả | HTTP Status | Thông báo lỗi |
| :--- | :--- | :--- | :--- |
| Email đã tồn tại | `Result.Failure` | `400 Bad Request` | `"Email '{email}' đã tồn tại trong hệ thống."` |
| Tenant không tồn tại | `Result.Failure` | `400 Bad Request` | `"Tổ chức (Tenant) với ID '{tenantId}' không tồn tại."` |
| Tenant bị vô hiệu hóa | `Result.Failure` | `400 Bad Request` | `"Tổ chức '{name}' hiện đang bị vô hiệu hóa."` |
| Phòng ban không thuộc Tenant | `Result.Failure` | `400 Bad Request` | `"Phòng ban với ID '{departmentId}' không tồn tại trong tổ chức."` |
| Phòng ban đã có Trưởng phòng | `Result.Failure` | `400 Bad Request` | `"Phòng ban '{name}' hiện đã có Trưởng phòng quản lý. Mỗi phòng ban chỉ được bổ nhiệm một Trưởng phòng."` |
| Quota yêu cầu vượt dung lượng phòng ban | `Result.Failure` | `400 Bad Request` | `"Hạn mức cá nhân yêu cầu ({x}) vượt quá dung lượng khả dụng của phòng ban '{name}' (Phòng còn trống: {y})."` |
| Quota yêu cầu vượt dung lượng Tenant | `Result.Failure` | `400 Bad Request` | `"Hạn mức yêu cầu ({x}) vượt quá dung lượng khả dụng của Tổ chức (Tenant còn trống: {y})."` |
| Hạn mức điều chỉnh là số âm | `Result.Failure` | `400 Bad Request` | `"Hạn mức dung lượng không được là số âm."` |
| Hạ quota thấp hơn dung lượng đã dùng | `Result.Failure` | `400 Bad Request` | `"Không thể hạ hạn mức phòng ban xuống thấp hơn dung lượng phòng ban đã sử dụng ({used})."` |
| Hạ quota thấp hơn tổng quota cấp cho nhân viên | `Result.Failure` | `400 Bad Request` | `"Không thể hạ hạn mức phòng ban xuống thấp hơn tổng hạn mức đã cấp cho các thành viên trong phòng ({total}). Hãy thu hồi bớt hạn mức của thành viên trước."` |
| Vượt quá dung lượng khả dụng của Tenant | `Result.Failure` | `400 Bad Request` | `"Vượt quá hạn mức lưu trữ khả dụng của Tổ chức! Tổ chức chỉ còn trống {available} để cấp thêm cho phòng ban này."` |
| Token kích hoạt không hợp lệ / hết hạn | `Result.Failure` / Ngoại lệ | `400 Bad Request` | `"Token không tồn tại."` / `"Token đã hết hạn."` / `"Token đã được sử dụng."` |

---

## 6. Acceptance Criteria (BDD Scenarios)

### Kịch bản 1: Bổ nhiệm Trưởng phòng thành công qua Email Invitation (Self-Setup)
```gherkin
Scenario: TenantAdmin mời Trưởng phòng mới theo mô hình Self-Setup
  Given TenantAdmin thuộc tổ chức "Acme Corp" đã đăng nhập
  And tổ chức còn trống 50 GB khả dụng
  When TenantAdmin gửi yêu cầu bổ nhiệm Trưởng phòng cho email "lead@acme.com" với quota 10 GB và để trống mật khẩu
  Then hệ thống tạo tài khoản User với trạng thái IsActive = false
  And hệ thống gán vai trò "DepartmentAdmin" cho người dùng
  And hệ thống cấp 10 GB vào UserStorageQuota như một ví giữ quota
  And hệ thống sinh EmailVerificationToken loại "DepartmentAdminInvitation" có hiệu lực 48 giờ
  And hệ thống gửi email kích hoạt tới "lead@acme.com"
  And phản hồi trả về kết quả thành công chứa thông tin UserDto
```

### Kịch bản 2: Từ chối bổ nhiệm khi phòng ban đã có Trưởng phòng
```gherkin
Scenario: Ngăn chặn bổ nhiệm nhiều hơn 1 Trưởng phòng cho một phòng ban
  Given phòng ban "Phòng Kỹ thuật" của "Acme Corp" đã có Trưởng phòng quản lý
  When TenantAdmin gửi yêu cầu bổ nhiệm thêm Trưởng phòng cho "Phòng Kỹ thuật"
  Then hệ thống từ chối yêu cầu
  And trả về lỗi: "Phòng ban 'Phòng Kỹ thuật' hiện đã có Trưởng phòng quản lý. Mỗi phòng ban chỉ được bổ nhiệm một Trưởng phòng."
  And không có tài khoản mới nào được tạo
```

### Kịch bản 3: Điều chỉnh tăng hạn mức lưu trữ cho phòng ban thành công
```gherkin
Scenario: Cấp thêm dung lượng lưu trữ cho phòng ban
  Given phòng ban "Phòng Kinh doanh" đang có hạn mức 20 GB
  And tổ chức "Acme Corp" còn 30 GB khả dụng chưa phân bổ
  When TenantAdmin điều chỉnh hạn mức phòng ban thành 35 GB
  Then hệ thống cập nhật StorageQuotaBytes của "Phòng Kinh doanh" thành 35 GB
  And hệ thống ghi AuditLog ghi nhận thay đổi từ 20 GB lên 35 GB
  And phản hồi trả về DepartmentStorageQuotaDto với hạn mức mới chính xác
```

### Kịch bản 4: Từ chối hạ hạn mức thấp hơn tổng quota đã cấp cho nhân viên
```gherkin
Scenario: Ngăn chặn hạ quota phòng ban thấp hơn tổng quota nhân viên đang nắm giữ
  Given phòng ban "Phòng Kế toán" có tổng hạn mức cấp cho các nhân viên là 15 GB
  And dung lượng file thực tế đã dùng là 5 GB
  When TenantAdmin cố gắng điều chỉnh hạn mức phòng ban xuống 10 GB
  Then hệ thống hủy giao dịch CSDL
  And trả về lỗi: "Không thể hạ hạn mức phòng ban xuống thấp hơn tổng hạn mức đã cấp cho các thành viên trong phòng (15 GB). Hãy thu hồi bớt hạn mức của thành viên trước."
  And hạn mức phòng ban được giữ nguyên giá trị cũ
```

### Kịch bản 5: Ngăn chặn TenantAdmin can thiệp vào tài nguyên của Tenant khác (IDOR)
```gherkin
Scenario: Chặn TenantAdmin truy cập hoặc sửa đổi phòng ban của tổ chức khác
  Given TenantAdmin thuộc tổ chức A
  And phòng ban "Phòng Marketing" thuộc tổ chức B
  When TenantAdmin gửi yêu cầu điều chỉnh quota cho phòng ban của tổ chức B
  Then hệ thống từ chối yêu cầu
  And trả về mã lỗi 400 Bad Request thông báo "Phòng ban không tồn tại trong tổ chức"
  And không có dữ liệu nào của tổ chức B bị thay đổi
```

---

## 7. Out of Scope

1. **Quản lý hạ tầng đám mây vật lý:** `TenantAdmin` không có quyền xem, tạo, xóa hoặc cấu hình các Storage Provider (Google Drive, OneDrive, Local Storage). Việc này thuộc đặc quyền của `SystemAdmin`.
2. **Phân phối quota chi tiết cho từng nhân viên:** Việc chia nhỏ quota của phòng ban cho từng nhân viên (`Employee`) là trách nhiệm của `DepartmentAdmin`, không thuộc phạm vi của `TenantAdmin`.
3. **Thanh toán gói dịch vụ:** Tính năng thanh toán trực tuyến hoặc mua thêm dung lượng cho Tenant không nằm trong phạm vi của module này.
4. **Quản lý tệp tin phòng ban:** `TenantAdmin` chỉ quản lý số liệu tổng quan (dung lượng, số lượng phòng ban), không có quyền can thiệp vào cấu trúc thư mục hoặc nội dung tài liệu mật nội bộ của từng phòng ban.

---

## 8. Feature Lock

- **Locked scope:** Toàn bộ các Use Case nghiệp vụ thuộc thư mục `CleanArchCqrs.Application/Features/TenantAdmin` (`CreateDepartmentAdmin`, `AllocateDepartmentStorageQuota`, `GetDepartments`) cùng Controller `DepartmentAdminsController` và `DepartmentsController`.
- **Deferred work:**
  - Quy trình giải thể phòng ban và thu hồi/chuyển đổi dữ liệu nhân sự tự động.
  - Tự động gửi cảnh báo qua Email cho TenantAdmin khi dung lượng Tenant hoặc phòng ban chạm ngưỡng 85% và 95%.
- **Change path:** `/sdd-update --feature=tenant-admin --artifact=spec --bump=<patch|minor|major> --reason="..."`
- **Lock boundary:** Feature `tenant-admin` only; không làm ảnh hưởng tới các phân hệ `system-admin`, `department-admin`, hoặc `Files`.

---

## 9. Adversarial Quality Pass

1. **Pre-mortem (Rủi ro tiềm ẩn):**
   - *Rủi ro:* Hai quản trị viên cùng lúc cấp quota cho 2 phòng ban khác nhau làm tổng quota vượt quá giới hạn của Tenant (Race Condition / Double Allocation).
   - *Khắc phục:* `AllocateDepartmentStorageQuotaCommandHandler` bao bọc toàn bộ khối logic kiểm tra và lưu dữ liệu trong Database Transaction với mức cô lập `IsolationLevel.Serializable`.
   - *Rủi ro:* Trưởng phòng được tạo ở chế độ Self-Setup giữ quota trong "ví tạm" quá lâu mà không tạo phòng ban, làm giam giữ dung lượng của Tenant.
   - *Khắc phục:* `CreateDepartmentAdminCommandHandler` tính toán cả số quota đang được giữ bởi các pending admin (`pendingAdminsQuota`) khi tính dung lượng khả dụng của Tenant, tránh tình trạng cấp phát vượt quá trần.
   - *Rủi ro:* Email server bị lỗi làm gián đoạn luồng bổ nhiệm Trưởng phòng.
   - *Khắc phục:* Sử dụng khối `try-catch` bọc ngoài `IEmailService.SendDepartmentAdminInvitationAsync`, ghi nhận token an toàn trong CSDL để có thể gửi lại liên kết mời mà không làm hỏng giao dịch tạo người dùng.

2. **Domain Walkthrough:**
   - Đã rà soát toàn bộ các Command và Query trong thư mục `CleanArchCqrs.Application/Features/TenantAdmin` và các Controller tương ứng tại `CleanArchCqrs.API/Controllers/Tenant`.
   - Các trường hợp biên (quota âm, quota nhỏ hơn dung lượng dùng, quota nhỏ hơn quota nhân viên, vượt quota Tenant, phòng ban đã có lead) đều có kịch bản EARS Unwanted và thông báo lỗi rõ ràng.

---

## 10. AI Recommendation và Human Final Review

### AI Recommendation
- **Methodology Profile:** `DETAILED`
- **Status:** `APPROVED`
- **EARS Compliance:** 100% Functional Requirements tuân thủ chuẩn 5 mẫu cú pháp EARS với đầy đủ các cặp Event-driven / Unwanted cho từng luồng nghiệp vụ.
- **Clean Architecture & Multi-Tenancy:** Tuân thủ triệt để nguyên tắc Clean Architecture: Chuyển sang 100% Email Invitation (Zero-Direct-Password); hỗ trợ tái tạo email tài khoản xóa mềm; tự động cấp toàn bộ quota khả dụng của Tenant khi Self-Setup; kiểm tra 3 lớp an toàn hạn mức và cô lập dữ liệu tổ chức (Anti-IDOR).

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả SPEC.md v1.1.0 cho phân hệ TenantAdmin.
- **Reviewed At:** 2026-10-14T00:30:00+07:00
- **Notes / Adjustments:** Phê duyệt toàn bộ chuẩn hóa Invitation-First và Self-Setup Quota allocation.
