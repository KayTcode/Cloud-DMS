# DepartmentAdmin Specification (SPEC.md)

- **Feature Slug:** `department-admin`
- **Version:** `1.2.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Phân hệ quản trị cấp phòng ban (`DepartmentAdmin`) sở hữu quyền tự thiết lập cơ cấu phòng ban (đọc trực tiếp Quota ví tạm UserStorageQuota không qua hardcode/fallback), quản lý nhân viên dưới quyền (`Employee`), và phân bổ hạn mức lưu trữ cá nhân (`UserStorageQuota`) tới từng người dùng cuối. Đòi hỏi tính nhất quán giao dịch CSDL (`Serializable`), bảo vệ dữ liệu đã sử dụng khi điều chỉnh quota, và ngăn chặn rò rỉ hoặc can thiệp dữ liệu chéo phòng ban (Department Isolation).
  - Risk Posture: `HIGH` (Quản lý hạn mức lưu trữ cá nhân, bảo vệ dữ liệu tệp tin, phân quyền nhân sự cấp cơ sở).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

### 1.1 Bối cảnh (Context)
Trong hệ sinh thái Cloud-DMS (Cloud Document Management System), phân hệ `DepartmentAdmin` (Trưởng phòng ban) đóng vai trò là tầng quản trị cấp cơ sở, trực tiếp tương tác với người dùng cuối (`Employee`):
- Được bổ nhiệm bởi `TenantAdmin` thông qua lời mời qua Email kèm theo một "ví quota" ban đầu.
- Tự quyết định cấu trúc phòng ban thông qua quy trình Self-Setup: chuyển đổi quota tạm thời thành quota chính thức của phòng ban, tự trích lại quota cá nhân cho mình, và hệ thống tự động sinh thư mục gốc mặc định cho phòng ban.
- Quản lý vòng đời nhân viên trong phòng: tạo tài khoản trực tiếp hoặc gửi thư mời kích hoạt qua Email (`EmployeeInvitation`), đồng thời cấp trước hạn mức lưu trữ ban đầu trích từ ngân sách khả dụng của phòng ban.
- Điều phối, nâng/hạ hạn mức lưu trữ cá nhân (`UserStorageQuota`) cho từng nhân viên, bảo đảm tuân thủ nguyên tắc không được hạ thấp hơn dung lượng thực tế nhân viên đã sử dụng.
- Theo dõi toàn diện tình hình sử dụng dung lượng lưu trữ của phòng ban thông qua bảng tổng hợp Quota chi tiết.

### 1.2 Mục tiêu (Goal)
- Đảm bảo tính toàn vẹn của mô hình phân cấp Quota 3 tầng (Tenant → Department → User): Tổng quota cấp cho các thành viên trong phòng ban không bao giờ được vượt quá quota của phòng ban đó.
- Bảo vệ dữ liệu cá nhân: Không cho phép hạ quota xuống thấp hơn dung lượng thực tế mà người dùng đã tải lên (`UsedBytes`), tránh tình trạng lỗi file hoặc gián đoạn hoạt động.
- Đảm bảo an toàn giao dịch: Toàn bộ quá trình cấp phát và điều chỉnh quota cá nhân phải được thực thi trong Database Transaction với mức cô lập `IsolationLevel.Serializable` để chống Race Condition khi có nhiều thao tác đồng thời.
- Thực thi nguyên tắc cô lập cấp phòng ban (Department-level Isolation): Trưởng phòng chỉ có quyền thao tác trên các thực thể và nhân viên thuộc đúng phòng ban của mình; ngăn chặn mọi hành vi can thiệp trái phép vào phòng ban khác.

---

## 2. Actors & Roles

| Actor / Role | Quyền hạn (Permissions) | Mô tả trách nhiệm |
| :--- | :--- | :--- |
| **DepartmentAdmin** | `department.create`, `user.create`, `user.read`, `storage.manage` (trong phạm vi phòng ban) | Trưởng phòng ban. Tự thiết lập phòng ban, quản lý nhân viên và phân bổ quota trong phòng của mình. |
| **TenantAdmin** | Quản trị tổ chức (`TenantAdmin.*`) | Cấp quota cho phòng ban, có quyền giám sát hoặc can thiệp quản trị phòng ban cấp trên. |
| **SystemAdmin** | Toàn quyền hệ thống (`SystemAdmin.*`) | Quản trị viên tối cao, có thể bypass các kiểm tra phạm vi phòng ban khi bảo trì. |
| **Employee** | Người dùng thông thường (`user.read.self`, `files.*`) | Nhân viên thuộc phòng ban, chỉ có quyền xem quota cá nhân và tải lên tệp tin trong hạn mức được cấp. |
| **Unauthorized / Other Dept** | Bị từ chối truy cập (`403 Forbidden` / `400 Bad Request`) | Không được phép xem hoặc điều chỉnh dữ liệu của phòng ban khác. |

---

## 3. Functional Requirements (EARS)

Phân hệ `DepartmentAdmin` bao gồm 5 Use Case chính được chuẩn hóa theo 5 mẫu cú pháp EARS (`Ubiquitous`, `Event-driven`, `State-driven`, `Optional`, `Unwanted`).

### 3.1 Tự Thiết lập Phòng ban (SetupDepartment)

#### UC-DA-01: Tự thiết lập Phòng ban từ Ví Quota tạm thời
- **EARS-01 (Event-driven):** `WHEN` DepartmentAdmin chưa có phòng ban gửi lệnh `SetupDepartmentCommand` với tên (`Name`) và mã (`Code`) hợp lệ, `THEN` hệ thống `SHALL` tạo thực thể `Department`, chuyển toàn bộ quota từ "ví tạm" (`UserStorageQuota`) sang phòng ban (`department.StorageQuotaBytes = adminQuota.QuotaBytes`), gắn Trưởng phòng vào phòng ban vừa tạo, trích quota cá nhân cho Trưởng phòng, tự động tạo hoặc liên kết thư mục gốc mặc định (`Folder`), ghi `AuditLog` và trả về `DepartmentDto`.
- **EARS-01A (Event-driven / Setup Info):** `WHEN` DepartmentAdmin gửi `GetDepartmentSetupInfoQuery` (`GET /api/department/setup-info`), `THEN` hệ thống `SHALL` truy xuất hạn mức ví tạm từ `UserStorageQuota` (`adminQuota.QuotaBytes`); giao diện người dùng `SHALL` sử dụng hạn mức này làm giá trị trần (`max`) cho thanh trượt chọn quota cá nhân.
- **EARS-02 (Optional):** `WHERE` yêu cầu chỉ định `AdminPersonalQuotaGB` hoặc `AdminPersonalQuotaBytes` hợp lệ, hệ thống `SHALL` cập nhật quota cá nhân của Trưởng phòng theo giá trị yêu cầu đó (bảo đảm không vượt quá tổng quota của phòng ban).
- **EARS-03 (Optional):** `WHERE` yêu cầu không chỉ định hạn mức cá nhân giữ lại, hệ thống `SHALL` mặc định giữ lại `Min(DepartmentQuota, 1 GB)` cho Trưởng phòng.
- **EARS-04 (Unwanted):** `IF` người dùng chưa xác thực danh tính hoặc không có vai trò `DepartmentAdmin` / thiếu claim `department.manage`, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Chỉ tài khoản Trưởng phòng (Department Admin) mới có quyền tự thiết lập phòng ban."`
- **EARS-05 (Unwanted):** `IF` tài khoản Trưởng phòng đã được gắn với một phòng ban (`user.DepartmentId != null`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Tài khoản của bạn đã được gắn với một Phòng ban, không thể thiết lập thêm phòng ban mới."`
- **EARS-06 (Unwanted):** `IF` mã phòng ban (`Code`) đã tồn tại trong tổ chức (không phân biệt hoa thường) hoặc sai định dạng Regex `^[a-zA-Z0-9_-]{2,50}$`, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi tương ứng.
- **EARS-07 (Unwanted):** `IF` hạn mức cá nhân giữ lại là số âm hoặc vượt quá tổng dung lượng của phòng ban (`personalQuota > departmentQuotaBytes`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Hạn mức cá nhân giữ lại ({personal}) không được vượt quá tổng dung lượng của phòng ban ({total})."`
- **EARS-07A (Unwanted):** `IF` tài khoản Trưởng phòng chưa có ví tạm hoặc `QuotaBytes <= 0`, `THEN` hệ thống `SHALL` từ chối thao tác với thông báo: `"Tài khoản của bạn chưa được cấp hạn mức lưu trữ (Quota). Vui lòng liên hệ Quản trị viên tổ chức để được cấp dung lượng."` (Loại bỏ hoàn toàn các giá trị hardcode hoặc fallback đọc từ Tenant).
- **EARS-08 (Ubiquitous):** Hệ thống `SHALL` tự động tạo một thư mục gốc cho phòng ban với `ParentFolderId = null`, `DepartmentId = department.Id`, và `Name = department.Name`. Nếu thư mục gốc cùng tên đã tồn tại trong Tenant, hệ thống `SHALL` liên kết thư mục đó với phòng ban thay vì tạo mới để bảo đảm tính duy nhất của khóa `(TenantId, ParentFolderId, Name)`.

---

### 3.2 Phân bổ và Điều chỉnh Hạn mức Cá nhân (AllocateUserStorageQuota)

#### UC-DA-02: Cấp phát và Điều chỉnh Hạn mức Lưu trữ cho Nhân viên
- **EARS-09 (Event-driven):** `WHEN` DepartmentAdmin gửi yêu cầu điều chỉnh hạn mức (`QuotaBytes`) cho một nhân viên thuộc phòng ban của mình, `THEN` hệ thống `SHALL` mở transaction CSDL với mức cô lập `IsolationLevel.Serializable`, kiểm tra các ràng buộc dung lượng, tạo mới hoặc cập nhật bản ghi `UserStorageQuota`, ghi `AuditLog` và trả về `UserStorageQuotaDto`.
- **EARS-10 (Unwanted):** `IF` hạn mức yêu cầu `QuotaBytes < 0`, `THEN` hệ thống `SHALL` từ chối thao tác và trả về lỗi: `"Hạn mức lưu trữ không được là số âm."`
- **EARS-11 (Unwanted):** `IF` người gọi thao tác trên một phòng ban khác với phòng ban mà mình quản lý (`_currentUserService.DepartmentId != request.DepartmentId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn không có quyền quản lý hạn mức của phòng ban này."` (Department Isolation).
- **EARS-12 (Unwanted):** `IF` nhân viên mục tiêu không tồn tại hoặc không thuộc phòng ban được chỉ định (`user.DepartmentId != request.DepartmentId`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi không tìm thấy người dùng thuộc phòng ban.
- **EARS-13 (Unwanted):** `IF` hạn mức mới `QuotaBytes` nhỏ hơn dung lượng thực tế mà người dùng đã sử dụng (`userQuota.UsedBytes`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi: `"Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng ({FormatBytes(userQuota.UsedBytes)})."` (Bảo vệ dữ liệu).
- **EARS-14 (Unwanted):** `IF` tổng hạn mức đã cấp cho các thành viên khác trong phòng cộng với `QuotaBytes` mới vượt quá tổng dung lượng của phòng ban (`department.StorageQuotaBytes`), `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi phòng ban không còn đủ hạn mức khả dụng để cấp thêm.
- **EARS-15 (Ubiquitous):** Hệ thống `SHALL` tự động ghi nhật ký kiểm toán `AuditLog` với `Action = "AllocateUserQuota"`, lưu vết chi tiết giá trị hạn mức cũ và mới cùng định dạng dung lượng thân thiện.

---

### 3.3 Tạo và Mời Nhân viên trong Phòng ban (CreateEmployee)

#### UC-DA-03: Tuyển dụng và Mời Nhân viên mới vào Phòng ban
- **EARS-16 (Event-driven):** `WHEN` DepartmentAdmin gửi thông tin tạo nhân viên mới (`Email`, `FirstName`, `LastName`) kèm theo hạn mức ban đầu hợp lệ vào phòng ban của mình, `THEN` hệ thống `SHALL` tạo thực thể `User`, gán vai trò `Employee`, gán `DepartmentId`, tạo `UserStorageQuota`, ghi `AuditLog` và trả về `UserDto`.
- **EARS-17 (Optional):** `WHERE` mật khẩu (`Password`) để trống trong yêu cầu, hệ thống `SHALL` xử lý dưới dạng lời mời (`isInvitation = true`), sinh mật khẩu ngẫu nhiên tạm thời, đặt `IsActive = false`, tạo `EmailVerificationToken` loại `EmployeeInvitation` có hiệu lực 48 giờ và gửi email kích hoạt tới nhân viên.
- **EARS-18 (Optional):** `WHERE` mật khẩu (`Password`) được cung cấp trực tiếp, hệ thống `SHALL` băm mật khẩu bằng BCrypt, đặt `IsActive = true`, `EmailConfirmed = true` và không gửi email kích hoạt.
- **EARS-19 (Unwanted):** `IF` Trưởng phòng cố gắng tạo nhân viên vào một phòng ban không phải do mình quản lý, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn chỉ có quyền tạo nhân viên trong phòng ban của mình."`
- **EARS-20 (Unwanted):** `IF` email của nhân viên đã tồn tại trên toàn hệ thống (không phân biệt chữ hoa/thường), `THEN` hệ thống `SHALL` từ chối thao tác với thông báo trùng lặp email.
- **EARS-21 (Unwanted):** `IF` hạn mức cấp ban đầu cho nhân viên vượt quá dung lượng khả dụng của phòng ban (`availableInDept = department.StorageQuotaBytes - totalAllocatedInDept`), `THEN` hệ thống `SHALL` từ chối thao tác và yêu cầu cấp thêm dung lượng cho phòng ban trước.
- **EARS-22 (Ubiquitous):** Lỗi gián đoạn gửi email (SMTP/Network failure) `SHALL NOT` làm hủy bỏ giao dịch tạo tài khoản nhân viên trong CSDL; token kích hoạt vẫn được lưu an toàn để gửi lại sau.

---

### 3.4 Xem Thống kê Hạn mức Lưu trữ Phòng ban (GetDepartmentUsersStorageQuota)

#### UC-DA-04: Báo cáo Tổng hợp Quota Nhân viên trong Phòng
- **EARS-23 (Event-driven):** `WHEN` DepartmentAdmin gửi yêu cầu xem hạn mức nhân viên theo `DepartmentId`, `THEN` hệ thống `SHALL` truy vấn toàn bộ người dùng đang hoạt động trong phòng ban, đối chiếu hạn mức `UserStorageQuota`, tính toán tổng dung lượng đã cấp (`TotalAllocatedToUsersBytes`) và dung lượng phòng ban còn lại chưa phân bổ (`UnallocatedDepartmentQuotaBytes`), trả về `DepartmentUsersQuotaSummaryDto`.
- **EARS-24 (Unwanted):** `IF` người gọi không phải là Trưởng phòng của phòng ban đó (và không phải Admin cấp cao hơn), `THEN` hệ thống `SHALL` từ chối và trả về lỗi không có quyền xem thông tin hạn mức của phòng ban này.

---

### 3.5 Danh sách Nhân viên Phân trang (GetDepartmentEmployees)

#### UC-DA-05: Truy vấn Danh sách Nhân viên Phân trang
- **EARS-25 (Event-driven):** `WHEN` DepartmentAdmin yêu cầu danh sách nhân viên trong phòng kèm tham số phân trang (`PageNumber`, `PageSize`, `SearchTerm`), `THEN` hệ thống `SHALL` thực thi truy vấn phân trang lọc theo `DepartmentId`, trả về danh sách `PaginatedList<UserDto>`.
- **EARS-26 (Ubiquitous):** Hệ thống `SHALL` luôn bảo đảm các nhân viên trả về thuộc đúng phòng ban được yêu cầu và thuộc cùng tổ chức (`TenantId`).
- **EARS-27 (Unwanted):** `IF` phòng ban không thuộc cùng tổ chức với người gọi (`department.TenantId != _currentUserService.TenantId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Phòng ban không thuộc tổ chức của bạn."` (Cross-Tenant Isolation).
- **EARS-28 (Unwanted):** `IF` người gọi không phải `SystemAdmin` hay `TenantAdmin` mà `DepartmentId` của người gọi là `null` hoặc không khớp với phòng ban mục tiêu, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn không có quyền quản lý hạn mức của phòng ban này."` hoặc `"Bạn không có quyền xem thông tin hạn mức của phòng ban này."` (Fail-Closed Department Isolation).
- **EARS-29 (Unwanted):** `IF` nhân viên được cấp quota không thuộc cùng tổ chức với phòng ban (`user.TenantId != department.TenantId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Người dùng không thuộc tổ chức này."` (Cross-Tenant User Isolation).
- **EARS-30 (Unwanted):** `IF` mã phòng ban (`Code`) trong yêu cầu thiết lập phòng ban chứa ký tự không hợp lệ hoặc có độ dài ngoài khoảng 2-50 ký tự, `THEN` hệ thống `SHALL` từ chối tại tầng Validation và trả về mã HTTP 400 Bad Request.
- **EARS-31 (Unwanted):** `IF` thông tin tạo nhân viên mới có `Email`, `FirstName`, `LastName` để trống hoặc `StorageQuotaBytes < 0`, `THEN` hệ thống `SHALL` từ chối tại tầng Validation và trả về mã HTTP 400 Bad Request.
- **EARS-32 (Ubiquitous):** Hệ thống `SHALL` luôn che giấu các thông điệp ngoại lệ cơ sở dữ liệu nội bộ (`ex.Message`), ghi log chi tiết có ngữ cảnh tại máy chủ qua `ILogger` và trả về thông điệp lỗi an toàn, thân thiện cho người dùng (Safe Error Contract theo `ENG-02`).

---

## 4. Non-Functional Requirements (NFR)

1. **NFR-SEC-01 (Cô lập cấp Phòng ban - Department Isolation):** Trưởng phòng tuyệt đối không được phép xem, tạo, sửa hoặc cấp quota cho nhân viên thuộc phòng ban khác (chống IDOR cấp phòng ban 100%).
2. **NFR-SEC-02 (Bảo mật Thư mời Nhân viên):** `EmailVerificationToken` cho nhân viên phải được sinh ngẫu nhiên với độ entropy cao, hết hạn sau **48 giờ**, và chỉ dùng được một lần duy nhất.
3. **NFR-SEC-03 (Cô lập cấp Tổ chức - Multi-Tenant Isolation):** Mọi vai trò (kể cả TenantAdmin) tuyệt đối không được phép xem hoặc can thiệp phòng ban/nhân viên thuộc Tenant khác.
4. **NFR-SEC-04 (Hợp đồng Lỗi An toàn - Safe Error Contract):** Hệ thống không bao giờ trả về stack trace hoặc exception message của database engine (`ex.Message`) ra API client.
5. **NFR-RELIABILITY-01 (Toàn vẹn Hạn mức Phòng ban):** Tổng hạn mức cấp phát cho các nhân viên trong phòng ban (`Sum(UserStorageQuota.QuotaBytes)`) không bao giờ được vượt quá `Department.StorageQuotaBytes`.
6. **NFR-RELIABILITY-02 (Bảo toàn Dữ liệu Upload):** Hệ thống không bao giờ cho phép hạ `UserStorageQuota.QuotaBytes` thấp hơn `UserStorageQuota.UsedBytes`.
7. **NFR-RELIABILITY-03 (Chống Race Condition):** Thao tác điều chỉnh quota cá nhân phải được thực thi trong Database Transaction với mức cô lập `IsolationLevel.Serializable`.
8. **NFR-PERF-01 (Hiệu năng Báo cáo Quota):** Endpoint `GetDepartmentUsersStorageQuota` phải phản hồi trong thời gian **< 150ms** đối với phòng ban có tối đa 500 nhân sự.
9. **NFR-AUDIT-01 (Truy vết Kiểm toán):** 100% các thao tác thiết lập phòng ban, tạo nhân viên và điều chỉnh hạn mức phải được ghi nhận vào bảng `AuditLogs`.

---

## 5. Data Model & Contracts

### 5.1 Các thực thể chính liên quan (Domain Entities)

- `Department`: Phòng ban (`Id`, `TenantId`, `Name`, `Code`, `Description`, `StorageQuotaBytes`, `StorageUsedBytes`, `IsActive`).
- `User`: Tài khoản người dùng (`Id`, `TenantId`, `DepartmentId`, `Email`, `PasswordHash`, `FirstName`, `LastName`, `IsActive`, `EmailConfirmed`).
- `UserRole`: Liên kết người dùng với vai trò `DepartmentAdmin` hoặc `Employee`.
- `UserStorageQuota`: Quản lý hạn mức cá nhân (`UserId`, `QuotaBytes`, `UsedBytes`).
- `Folder`: Quản lý thư mục (Thư mục gốc phòng ban có `ParentFolderId = null`, `DepartmentId = department.Id`).
- `EmailVerificationToken`: Token kích hoạt tài khoản (`Token`, `UserId`, `TokenType = "EmployeeInvitation"`, `ExpiresAt`, `IsUsed`).
- `AuditLog`: Bản ghi kiểm toán hành vi quản trị (`TenantId`, `UserId`, `Action`, `EntityName`, `EntityId`, `Details`, `CreatedAt`).

### 5.2 Data Transfer Objects (DTOs)

#### `UserStorageQuotaDto`
```csharp
public class UserStorageQuotaDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; }
    public long QuotaBytes { get; set; }
    public long UsedBytes { get; set; }
    public long AvailableBytes => Math.Max(0, QuotaBytes - UsedBytes);
    public double UsagePercentage => ...;
}
```

#### `DepartmentUsersQuotaSummaryDto`
```csharp
public class DepartmentUsersQuotaSummaryDto
{
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; }
    public long DepartmentQuotaBytes { get; set; }
    public long DepartmentUsedBytes { get; set; }
    public long TotalAllocatedToUsersBytes { get; set; }
    public long UnallocatedDepartmentQuotaBytes => Math.Max(0, DepartmentQuotaBytes - TotalAllocatedToUsersBytes);
    public List<UserStorageQuotaDto> Users { get; set; }
}
```

#### `DepartmentSetupInfoDto`
```csharp
public class DepartmentSetupInfoDto
{
    public long AssignedQuotaBytes { get; set; }
    public long AssignedQuotaGB => AssignedQuotaBytes / (1024L * 1024L * 1024L);
    public string FormattedQuota { get; set; }
    public bool HasAssignedQuota => AssignedQuotaBytes > 0;
}
```

### 5.3 Error Contracts

| Tình huống vi phạm | Kiểu kết quả | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- | :--- |
| Không đúng vai trò DepartmentAdmin | `Result.Failure` | `400 Bad Request` | `"Chỉ tài khoản Trưởng phòng (Department Admin) mới có quyền tự thiết lập phòng ban."` |
| Đã có phòng ban gắn với tài khoản | `Result.Failure` | `400 Bad Request` | `"Tài khoản của bạn đã được gắn với một Phòng ban, không thể thiết lập thêm phòng ban mới."` |
| Ví tạm chưa có Quota hoặc QuotaBytes <= 0 | `Result.Failure` | `400 Bad Request` | `"Tài khoản của bạn chưa được cấp hạn mức lưu trữ (Quota). Vui lòng liên hệ Quản trị viên tổ chức để được cấp dung lượng."` |
| Mã phòng ban trùng lặp trong Tenant | `Result.Failure` | `400 Bad Request` | `"Mã phòng ban '{code}' đã tồn tại trong tổ chức của bạn."` |
| Xung đột Unique Index CSDL | `Result.Failure` | `400 Bad Request` | `"Không thể lưu phòng ban. Mã phòng '{Code}' hoặc Tên phòng '{Name}' có thể đã tồn tại trong hệ thống."` |
| Hạn mức cá nhân giữ lại vượt quota phòng | `Result.Failure` | `400 Bad Request` | `"Hạn mức cá nhân giữ lại ({x}) không được vượt quá tổng dung lượng của phòng ban ({y})."` |
| Thao tác trên phòng ban khác | `Result.Failure` | `400 Bad Request` | `"Bạn không có quyền quản lý hạn mức của phòng ban này."` / `"Bạn chỉ có quyền tạo nhân viên trong phòng ban của mình."` |
| Phòng ban không thuộc tổ chức của caller | `Result.Failure` | `400 Bad Request` | `"Phòng ban không thuộc tổ chức của bạn."` |
| Nhân viên không thuộc phòng ban | `Result.Failure` | `400 Bad Request` | `"Người dùng không thuộc phòng ban được chỉ định."` |
| Nhân viên không thuộc tổ chức của phòng | `Result.Failure` | `400 Bad Request` | `"Người dùng không thuộc tổ chức này."` |
| Hạ quota thấp hơn dung lượng đã dùng | `Result.Failure` | `400 Bad Request` | `"Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng ({used})."` |
| Vượt quá hạn mức phòng ban | `Result.Failure` | `400 Bad Request` | `"Phòng ban '{name}' không còn đủ hạn mức để cấp cho nhân viên này! Hạn mức phòng ban còn lại: {x} (Yêu cầu: {y})."` |
| Trùng lặp email nhân viên | `Result.Failure` | `400 Bad Request` | `"Email '{email}' đã tồn tại trong hệ thống."` |
| Hạn mức điều chỉnh là số âm | `Result.Failure` | `400 Bad Request` | `"Hạn mức lưu trữ không được là số âm."` |
| Lỗi CSDL nội bộ khi phân bổ quota | `Result.Failure` | `400 Bad Request` | `"Đã xảy ra lỗi trong quá trình điều phối hạn mức lưu trữ. Vui lòng thử lại sau."` |

---

## 6. Acceptance Criteria (BDD Scenarios)

### Kịch bản 1: Trưởng phòng tự thiết lập phòng ban thành công (Self-Setup)
```gherkin
Scenario: DepartmentAdmin tự thiết lập phòng ban mới từ ví quota tạm
  Given DepartmentAdmin đã kích hoạt tài khoản và nắm giữ 20 GB trong ví quota tạm thời
  And tài khoản chưa gắn với bất kỳ phòng ban nào (DepartmentId = null)
  When DepartmentAdmin gửi yêu cầu thiết lập phòng ban với tên "Phòng Kỹ thuật", mã "DEPT_TECH" và giữ lại 2 GB cho cá nhân
  Then hệ thống tạo thực thể Department "Phòng Kỹ thuật" với StorageQuotaBytes = 20 GB
  And hệ thống cập nhật UserStorageQuota của Trưởng phòng thành 2 GB
  And hệ thống gắn tài khoản Trưởng phòng vào phòng ban mới
  And hệ thống tự động tạo thư mục gốc "Phòng Kỹ thuật" với ParentFolderId = null
  And hệ thống ghi AuditLog lưu vết thiết lập
  And phản hồi trả về kết quả thành công DepartmentDto
```

### Kịch bản 2: Điều chỉnh cấp thêm Quota cho nhân viên thành công
```gherkin
Scenario: Trưởng phòng cấp thêm hạn mức lưu trữ cho nhân viên
  Given phòng ban "Phòng Kỹ thuật" có tổng quota 20 GB, các thành viên khác đang dùng 10 GB
  And nhân viên "Nguyen Van A" đang có hạn mức 2 GB, đã dùng thực tế 1 GB
  When Trưởng phòng điều chỉnh hạn mức của "Nguyen Van A" thành 5 GB
  Then hệ thống mở giao dịch Serializable
  And cập nhật QuotaBytes của "Nguyen Van A" thành 5 GB
  And ghi nhận AuditLog chi tiết từ 2 GB lên 5 GB
  And trả về UserStorageQuotaDto với hạn mức mới chính xác
```

### Kịch bản 3: Từ chối hạ Quota thấp hơn dung lượng nhân viên đã dùng
```gherkin
Scenario: Chặn hạ quota thấp hơn dung lượng nhân viên đã tải lên
  Given nhân viên "Tran Van B" có hạn mức 5 GB và đã tải lên 3.5 GB dữ liệu
  When Trưởng phòng cố gắng hạ hạn mức của "Tran Van B" xuống 2 GB
  Then hệ thống hủy giao dịch
  And trả về lỗi: "Không thể hạ hạn mức cá nhân xuống thấp hơn dung lượng người dùng đã sử dụng (3.5 GB)."
  And hạn mức của nhân viên vẫn giữ nguyên 5 GB
```

### Kịch bản 4: Từ chối cấp Quota vượt quá khả năng của phòng ban
```gherkin
Scenario: Chặn cấp quota vượt quá dung lượng còn lại của phòng ban
  Given phòng ban có hạn mức 20 GB, tổng quota đã cấp cho các thành viên là 18 GB
  When Trưởng phòng yêu cầu cấp 5 GB cho nhân viên mới
  Then hệ thống từ chối yêu cầu
  And trả về lỗi: "Phòng ban không còn đủ hạn mức để cấp cho nhân viên này! Hạn mức phòng ban còn lại: 2 GB (Yêu cầu: 5 GB)."
  And không có hạn mức nào bị thay đổi
```

### Kịch bản 5: Chặn Trưởng phòng can thiệp vào nhân viên phòng ban khác (Department Isolation)
```gherkin
Scenario: Ngăn chặn Trưởng phòng cấp quota cho nhân viên phòng ban khác
  Given Trưởng phòng thuộc phòng ban A
  And nhân viên thuộc phòng ban B
  When Trưởng phòng gửi yêu cầu điều chỉnh quota cho nhân viên phòng ban B
  Then hệ thống chặn thao tác
  And trả về lỗi "Bạn không có quyền quản lý hạn mức của phòng ban này."
  And không có dữ liệu nào bị thay đổi
```

### Kịch bản 6: Chặn TenantAdmin can thiệp vào phòng ban tổ chức khác (Cross-Tenant Protection)
```gherkin
Scenario: Chặn TenantAdmin điều chỉnh quota của phòng ban thuộc Tenant khác
  Given TenantAdmin thuộc Tổ chức Alpha
  And phòng ban và nhân viên thuộc Tổ chức Beta
  When TenantAdmin Alpha gửi yêu cầu điều chỉnh quota cho nhân viên tại Tổ chức Beta
  Then hệ thống chặn thao tác
  And trả về lỗi "Phòng ban không thuộc tổ chức của bạn."
  And không có dữ liệu nào bị thay đổi
```

### Kịch bản 7: Chặn yêu cầu cấp quota âm qua FluentValidation
```gherkin
Scenario: Chặn yêu cầu tạo nhân viên với hạn mức lưu trữ âm
  Given DepartmentAdmin gửi yêu cầu tạo nhân viên mới
  And tham số StorageQuotaBytes có giá trị -500 B
  When yêu cầu đi qua tầng Validation
  Then hệ thống từ chối yêu cầu với mã lỗi HTTP 400 Bad Request
  And trả về thông báo lỗi "Hạn mức lưu trữ không được là số âm."
  And không có người dùng nào được tạo trong CSDL
```

---

## 7. Out of Scope

1. **Điều chỉnh tổng hạn mức phòng ban:** Trưởng phòng không thể tự nâng tổng quota của phòng ban (`Department.StorageQuotaBytes`); việc này phải do `TenantAdmin` thực hiện.
2. **Quản lý danh sách phòng ban toàn tổ chức:** Trưởng phòng chỉ quản lý duy nhất phòng ban của mình, không có quyền xem hay cấu hình phòng ban khác.
3. **Cấu hình hạ tầng lưu trữ đám mây:** Quyền hạn này thuộc `SystemAdmin`.
4. **Xóa phòng ban:** Trưởng phòng không có quyền giải thể phòng ban.

---

## 8. Feature Lock

- **Locked scope:** Toàn bộ các Use Case nghiệp vụ thuộc thư mục `CleanArchCqrs.Application/Features/DepartmentAdmin` (`SetupDepartment`, `AllocateUserStorageQuota`, `CreateEmployee`, `GetDepartmentUsersStorageQuota`) cùng các Controller tại `CleanArchCqrs.API/Controllers/Department/`.
- **Deferred work:**
  - Tự động gửi cảnh báo qua Email cho Trưởng phòng khi tổng quota cấp cho nhân viên chạm ngưỡng 95% quota phòng ban.
  - Luồng bàn giao quyền Trưởng phòng cho một nhân viên khác khi Trưởng phòng chuyển công tác.
- **Change path:** `/sdd-update --feature=department-admin --artifact=spec --bump=<patch|minor|major> --reason="..."`
- **Lock boundary:** Feature `department-admin` only; không ảnh hưởng tới `system-admin`, `tenant-admin` hay `Files`.

---

## 9. Adversarial Quality Pass

1. **Pre-mortem (Rủi ro tiềm ẩn):**
   - *Rủi ro:* Trưởng phòng thao tác cấp quota đồng thời cho 2 nhân viên qua 2 tab trình duyệt làm tổng quota vượt quá hạn mức phòng ban (Race Condition).
   - *Khắc phục:* `AllocateUserStorageQuotaCommandHandler` bao bọc toàn bộ khối kiểm tra và ghi dữ liệu trong Database Transaction với mức cô lập `IsolationLevel.Serializable`.
   - *Rủi ro:* Hạ quota của nhân viên xuống thấp làm lỗi upload file hoặc file bị treo trạng thái không hợp lệ.
   - *Khắc phục:* Bắt buộc kiểm tra `request.QuotaBytes >= userQuota.UsedBytes` trước khi cho phép lưu dữ liệu.
   - *Rủi ro:* Trưởng phòng gửi thư mời nhưng nhân viên không nhận được email do lỗi máy chủ SMTP.
   - *Khắc phục:* Bọc `_emailService.SendUserActivationEmailAsync` trong khối `try-catch`; token và người dùng vẫn được lưu an toàn trong CSDL để có thể gửi lại link kích hoạt.

2. **Domain Walkthrough:**
   - Đã rà soát đầy đủ 4 Command/Query handler trong thư mục `CleanArchCqrs.Application/Features/DepartmentAdmin`.
   - Mọi tình huống ngoại lệ (quota âm, quota nhỏ hơn dung lượng dùng, vượt quota phòng, trùng email, trùng mã phòng, sai quyền phòng ban) đều có kịch bản EARS Unwanted và thông báo lỗi rõ ràng.

---

## 10. AI Recommendation và Human Final Review

### AI Recommendation
- **Methodology Profile:** `DETAILED`
- **Status:** `APPROVED`
- **EARS Compliance:** 100% Functional Requirements tuân thủ chuẩn 5 mẫu cú pháp EARS với đầy đủ các cặp Event-driven / Unwanted cho từng luồng nghiệp vụ.
- **Clean Architecture & Department Isolation:** Tuân thủ triệt để nguyên tắc Clean Architecture: Tách biệt toàn bộ file Command/Query/Handler; loại bỏ hoàn toàn fallback hardcode/Tenant; đọc trực tiếp quota từ ví tạm `UserStorageQuota`; xử lý chống xung đột Unique Index thư mục gốc và chuẩn hóa phân quyền policy `department.manage`.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả SPEC.md v1.2.0 cho phân hệ DepartmentAdmin.
- **Reviewed At:** 2026-10-14T00:30:00+07:00
- **Notes / Adjustments:** Phê duyệt toàn bộ cập nhật kiến trúc động hóa Quota và phân tách Command/Query.
