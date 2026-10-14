# SystemAdmin Specification (SPEC.md)

- **Feature Slug:** `system-admin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Phân hệ quản trị cấp cao (`SystemAdmin`) sở hữu các đặc quyền tối thượng của toàn bộ hệ sinh thái Cloud-DMS (quản lý Tenant, tài khoản liên tổ chức, cấu hình phân bổ hạn mức dung lượng, và liên kết Cloud Storage Provider qua OAuth2/AES-256).
  - Risk Posture: `HIGH` (Tác động tới toàn hệ thống, bảo mật credentials, tính toàn vẹn đa tổ chức / Multi-Tenancy).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

### 1.1 Bối cảnh (Context)
Trong hệ thống Cloud-DMS (Cloud Document Management System), phân hệ `SystemAdmin` (Quản trị hệ thống) đóng vai trò là tầng quản trị cấp cao nhất (Super Admin). Khác với các Quản trị viên cấp Tenant (`TenantAdmin`) bị giới hạn dữ liệu trong phạm vi tổ chức của mình, `SystemAdmin` có thẩm quyền xuyên suốt toàn bộ nền tảng:
- Quản lý vòng đời tài khoản người dùng và gán vai trò (`SystemAdmin`, `TenantAdmin`, `User`,...).
- Khởi tạo và cấp phát Tenant mới đi kèm hạ tầng lưu trữ.
- Kết nối, kiểm thử và quản lý các nhà cung cấp lưu trữ đám mây (`StorageProvider`: Local, Google Drive, Microsoft OneDrive).
- Cấp phát và điều tiết hạn mức dung lượng (`StorageQuota`) cho từng Tenant từ quỹ lưu trữ chung của các Provider.
- Giám sát toàn bộ hoạt động hệ thống thông qua truy vết Audit Logs tập trung.

### 1.2 Mục tiêu (Goal)
- Đảm bảo tính toàn vẹn dữ liệu đa tổ chức, ngăn ngừa tình trạng cạn kiệt tài nguyên lưu trữ của Tenant.
- Bảo mật tuyệt đối thông tin xác thực (`credentials`, `refresh_tokens`) của các Storage Provider thông qua mã hóa AES-256 đối xứng.
- Đảm bảo an toàn vận hành: Không cho phép tự vô hiệu hóa hoặc xóa SystemAdmin hoạt động duy nhất; tự động thu hồi phiên đăng nhập (`RefreshTokens`) khi có thay đổi bảo mật (đổi mật khẩu, khóa tài khoản, phân quyền lại).

---

## 2. Actors & Roles

| Actor / Role | Quyền hạn (Permissions) | Mô tả trách nhiệm |
| :--- | :--- | :--- |
| **SystemAdmin** | Toàn quyền (`SystemAdmin.Users.*`, `SystemAdmin.Storage.*`, `SystemAdmin.AuditLogs.View`) | Người quản trị cấp cao nhất của hệ thống DMS. Thao tác trên mọi Tenant, User, Provider và Log. |
| **System Background Worker / Scheduler** | Service Account / Internal | Tự động đồng bộ dung lượng đã dùng, kiểm tra trạng thái kết nối Cloud Provider định kỳ. |
| **Unauthorized User / Other Roles** | Bị từ chối truy cập (`403 Forbidden` / `401 Unauthorized`) | Không có quyền thực thi bất kỳ Command hay Query nào trong phân hệ này. |

---

## 3. Functional Requirements (EARS)

Phân hệ `SystemAdmin` bao gồm 17 Use Case chính, được chuẩn hóa theo 5 mẫu cú pháp EARS (`Ubiquitous`, `Event-driven`, `State-driven`, `Optional`, `Unwanted`).

### 3.1 Nhóm Quản lý Người dùng & Danh tính (User & Identity Management)

#### UC-SA-01: Tạo Người dùng mới (CreateUser)
- **EARS-01 (Event-driven):** `WHEN` SystemAdmin gửi yêu cầu tạo người dùng với email hợp lệ và chưa tồn tại, `THEN` hệ thống `SHALL` tạo mới bản ghi `User`, băm mật khẩu bằng BCrypt (hoặc tạo mật khẩu ngẫu nhiên), gán các vai trò tương ứng và trả về `UserDto`.
- **EARS-02 (Optional):** `WHERE` yêu cầu bao gồm `TenantName` chưa tồn tại, hệ thống `SHALL` tự động khởi tạo Tenant mới, sinh mã `TenantCode` duy nhất và liên kết người dùng với vai trò quản trị viên của Tenant đó.
- **EARS-03 (Optional):** `WHERE` yêu cầu bao gồm `StorageProviderId` và `StorageQuotaBytes` hợp lệ khi tạo Tenant, hệ thống `SHALL` cấp phát hạn mức dung lượng ngay cho Tenant mới.
- **EARS-04 (Unwanted):** `IF` email người dùng đã tồn tại trong hệ thống (không phân biệt chữ hoa/thường), `THEN` hệ thống `SHALL` từ chối yêu cầu và trả về lỗi `DuplicateEmailException` (`Result.Failure`).
- **EARS-05 (Unwanted):** `IF` dung lượng `StorageQuotaBytes` yêu cầu vượt quá dung lượng khả dụng của `StorageProvider`, `THEN` hệ thống `SHALL` hủy transaction và trả về lỗi vượt hạn mức khả dụng.

#### UC-SA-02: Cập nhật Thông tin Người dùng (UpdateUser)
- **EARS-06 (Event-driven):** `WHEN` SystemAdmin gửi thông tin cập nhật (Họ, Tên, Số điện thoại, Trạng thái kích hoạt) cho một người dùng hợp lệ, `THEN` hệ thống `SHALL` cập nhật dữ liệu, ghi nhận `UpdatedAt` và trả về `UserDto` mới nhất.
- **EARS-07 (Unwanted):** `IF` mã định danh người dùng (`Id`) không tồn tại trong hệ thống, `THEN` hệ thống `SHALL` trả về lỗi `UserNotFoundException` (`Result.Failure`).

#### UC-SA-03: Thay đổi Trạng thái Kích hoạt Người dùng (ChangeUserStatus)
- **EARS-08 (Event-driven):** `WHEN` SystemAdmin yêu cầu vô hiệu hóa (`IsActive = false`) một tài khoản người dùng thông thường, `THEN` hệ thống `SHALL` cập nhật `IsActive = false`, tăng `AuthorizationVersion` thêm 1, và thu hồi toàn bộ `RefreshTokens` còn hiệu lực của tài khoản đó với lý do `user_deactivated`.
- **EARS-09 (Event-driven):** `WHEN` SystemAdmin yêu cầu kích hoạt lại (`IsActive = true`) một tài khoản, `THEN` hệ thống `SHALL` cập nhật `IsActive = true` và tăng `AuthorizationVersion`.
- **EARS-10 (Unwanted):** `IF` tài khoản bị yêu cầu vô hiệu hóa là tài khoản `SystemAdmin` duy nhất đang hoạt động trong toàn hệ sinh thái, `THEN` hệ thống `SHALL` từ chối thao tác và trả về thông báo lỗi cấm vô hiệu hóa Admin duy nhất.

#### UC-SA-04: Xóa Tài khoản Người dùng (DeleteUser - Soft Delete)
- **EARS-11 (Event-driven):** `WHEN` SystemAdmin gửi lệnh xóa tài khoản người dùng, `THEN` hệ thống `SHALL` thực hiện Soft-Delete bằng cách đặt `IsActive = false`, thu hồi toàn bộ `RefreshTokens`, và giữ nguyên dữ liệu thực thể để bảo toàn tính toàn vẹn khóa ngoại (AuditLogs, FileEntries).
- **EARS-12 (Unwanted):** `IF` tài khoản bị yêu cầu xóa là tài khoản `SystemAdmin` duy nhất trong hệ thống, `THEN` hệ thống `SHALL` từ chối xóa và trả về thông báo lỗi vi phạm an toàn vận hành.

#### UC-SA-05: Gán Vai trò và Đơn vị cho Người dùng (AssignUserRoles)
- **EARS-13 (Event-driven):** `WHEN` SystemAdmin gửi danh sách `RoleIds`, `TenantId` và `DepartmentId` hợp lệ cho một người dùng, `THEN` hệ thống `SHALL` đồng bộ lại liên kết `UserRoles`, cập nhật `TenantId` và `DepartmentId`, tăng `AuthorizationVersion` thêm 1 để ép client làm mới quyền, và trả về `UserDto`.
- **EARS-14 (Unwanted):** `IF` `DepartmentId` được chỉ định không thuộc về `TenantId` đã chọn, `THEN` hệ thống `SHALL` từ chối và trả về lỗi không đồng nhất cấu trúc tổ chức.
- **EARS-15 (Unwanted):** `IF` một hoặc nhiều `RoleId` trong danh sách không tồn tại trong cơ sở dữ liệu, `THEN` hệ thống `SHALL` từ chối và báo lỗi vai trò không hợp lệ.

#### UC-SA-06: Đặt lại Mật khẩu Người dùng (ResetUserPassword)
- **EARS-16 (Event-driven):** `WHEN` SystemAdmin cung cấp mật khẩu mới cho tài khoản người dùng, `THEN` hệ thống `SHALL` băm mật khẩu mới bằng thuật toán bảo mật, lưu vào trường `PasswordHash`, đồng thời thu hồi (`Revoke`) tất cả `RefreshTokens` đang hoạt động của người dùng đó với lý do `password_reset`.
- **EARS-17 (Unwanted):** `IF` mật khẩu mới không thỏa mãn chính sách độ phức tạp mật khẩu hệ thống, `THEN` hệ thống `SHALL` trả về lỗi xác thực validation trước khi xử lý.

#### UC-SA-07: Truy vấn Danh sách Người dùng Phân trang (GetUsersWithPagination)
- **EARS-18 (Event-driven):** `WHEN` SystemAdmin truy vấn danh sách người dùng kèm bộ lọc (`SearchTerm`, `RoleId`, `TenantId`, `DepartmentId`, `IsActive`, `PageNumber`, `PageSize`), `THEN` hệ thống `SHALL` trả về danh sách phân trang `PaginatedList<UserDto>` chứa thông tin hồ sơ, vai trò, quyền hạn và tổ chức.

#### UC-SA-08: Lấy Chi tiết Người dùng theo ID (GetUserById)
- **EARS-19 (Event-driven):** `WHEN` SystemAdmin yêu cầu xem thông tin chi tiết người dùng bằng `Id`, `THEN` hệ thống `SHALL` nạp thông tin người dùng cùng các quan hệ Eager-loaded (Tenant, Department, UserRoles, RolePermissions, Permissions) và trả về `UserDto`.
- **EARS-20 (Unwanted):** `IF` `Id` người dùng không tồn tại, `THEN` hệ thống `SHALL` trả về `Result.Failure` thông báo không tìm thấy.

---

### 3.2 Nhóm Quản lý Nhà cung cấp Lưu trữ Đám mây (Cloud Storage Provider Management)

#### UC-SA-09: Kiểm thử Kết nối Storage Provider (TestStorageProviderConnection)
- **EARS-21 (Event-driven):** `WHEN` SystemAdmin gửi thông tin cấu hình (`StorageProviderType`, `ConfigJson`) để kiểm tra, `THEN` hệ thống `SHALL` khởi tạo adapter tạm thời, kiểm tra kết nối tới dịch vụ lưu trữ (Local, Google Drive, OneDrive), đo độ trễ (`LatencyMs`) và trả về `TestConnectionResult` kèm dung lượng khả dụng thực tế.
- **EARS-22 (Unwanted):** `IF` thông tin cấu hình sai (sai Token, sai Private Key, không đủ quyền API), `THEN` hệ thống `SHALL` bắt ngoại lệ và trả về `TestConnectionResult(Success: false, Message: <Chi tiết lỗi>)` mà không làm sập ứng dụng.

#### UC-SA-10: Kết nối và Lưu mới Storage Provider (ConnectStorageProvider)
- **EARS-23 (Event-driven):** `WHEN` SystemAdmin kết nối một Cloud Provider mới với tên và cấu hình JSON hợp lệ, `THEN` hệ thống `SHALL` kiểm tra dung lượng thực tế qua adapter, tự động tạo thư mục gốc `CloudDMS_Root` trên đám mây, mã hóa chuỗi cấu hình bằng AES-256 (`ConfigEncrypted`), lưu vào bảng `StorageProviders` và trả về `StorageProviderDto`.
- **EARS-24 (Unwanted):** `IF` việc kiểm tra kết nối đám mây thất bại hoặc thông tin cấu hình không hợp lệ, `THEN` hệ thống `SHALL` không lưu bản ghi vào cơ sở dữ liệu và trả về lỗi kết nối.

#### UC-SA-11: Trao đổi Mã Ủy quyền Microsoft OneDrive OAuth2 (ExchangeOneDriveCode)
- **EARS-25 (Event-driven):** `WHEN` SystemAdmin gửi mã Authorization Code từ Microsoft OAuth2 callback, `THEN` hệ thống `SHALL` gửi request tới endpoint Microsoft Identity Platform để đổi lấy `access_token` và `refresh_token`, mã hóa cấu hình an toàn bằng AES-256, lưu hoặc cập nhật bản ghi `StorageProvider` mang tên "Microsoft OneDrive" và kích hoạt hoạt động.
- **EARS-26 (Unwanted):** `IF` phản hồi từ Microsoft không chứa `refresh_token` (do thiếu scope `offline_access` hoặc hết hạn code), `THEN` hệ thống `SHALL` từ chối lưu và trả về cảnh báo yêu cầu cấp lại quyền.
- **EARS-27 (Ubiquitous):** Hệ thống `SHALL` tuyệt đối không bao giờ trả về chuỗi cấu hình giải mã hoặc token bí mật về phía trình duyệt / client API trong response payload.

#### UC-SA-12: Xóa Storage Provider (DeleteStorageProvider)
- **EARS-28 (Event-driven):** `WHEN` SystemAdmin gửi yêu cầu xóa một `StorageProvider`, `THEN` hệ thống `SHALL` gỡ liên kết (`StorageProviderId = null`) của tất cả các Tenant đang trỏ tới Provider này, xóa bản ghi Provider khỏi CSDL và trả về `true`.
- **EARS-29 (Unwanted):** `IF` mã định danh `StorageProvider` không tồn tại, `THEN` hệ thống `SHALL` trả về `false`.

#### UC-SA-13: Lấy Danh sách Tất cả Storage Providers (GetStorageProviders)
- **EARS-30 (Event-driven):** `WHEN` SystemAdmin yêu cầu danh sách các Storage Providers, `THEN` hệ thống `SHALL` trả về toàn bộ danh sách kèm các chỉ số thống kê tổng hợp: Tổng dung lượng, Dung lượng đã dùng thực tế, Dung lượng đã cấp phát cho các Tenant (`AllocatedToTenantsBytes`), và số lượng Tenant đang sử dụng.

#### UC-SA-14: Lấy Dung lượng Khả dụng để Cấp phát của Provider (GetStorageProviderAvailableQuota)
- **EARS-31 (Event-driven):** `WHEN` SystemAdmin truy vấn hạn mức khả dụng của một Provider cụ thể, `THEN` hệ thống `SHALL` tính toán công thức: `AvailableToAllocate = Max(0, CloudFreeBytes - Sum(TenantQuotaAllocated))` và trả về `StorageProviderAvailableQuotaDto`.
- **EARS-32 (Unwanted):** `IF` Provider không tồn tại trong hệ thống, `THEN` hệ thống `SHALL` trả về kết quả `null`.

---

### 3.3 Nhóm Quản lý Hạn mức Tenant & Giám sát Hệ thống (Tenant Quota & System Monitoring)

#### UC-SA-15: Cấp phát Hạn mức Lưu trữ cho Tenant (AllocateTenantStorageQuota)
- **EARS-33 (Event-driven):** `WHEN` SystemAdmin phân bổ dung lượng `QuotaBytes` từ một `StorageProvider` cho một `Tenant`, `THEN` hệ thống `SHALL` kiểm tra tính hợp lệ của hạn mức, gán `StorageProviderId` và `StorageQuotaBytes` cho Tenant, lưu vào DB và trả về `TenantStorageQuotaDto`.
- **EARS-34 (Unwanted):** `IF` `QuotaBytes` yêu cầu vượt quá dung lượng khả dụng chưa phân bổ của Provider (`AvailableToAllocateBytes`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi `InvalidOperationException` thông báo vượt hạn mức Cloud.
- **EARS-35 (Unwanted):** `IF` `QuotaBytes` yêu cầu nhỏ hơn dung lượng thực tế Tenant đã tải lên (`StorageUsedBytes`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi cấm hạ hạn mức thấp hơn dung lượng đang sử dụng.
- **EARS-36 (Unwanted):** `IF` `StorageProvider` đang trong trạng thái bị vô hiệu hóa (`IsActive = false`), `THEN` hệ thống `SHALL` từ chối cấp phát.

#### UC-SA-16: Lấy Thông tin Hạn mức Lưu trữ của Tenant (GetTenantStorageQuota)
- **EARS-37 (Event-driven):** `WHEN` SystemAdmin truy vấn thông tin hạn mức của một Tenant theo `TenantId`, `THEN` hệ thống `SHALL` trả về thông tin chi tiết gồm: Tên/Mã Tenant, Tên/Loại Provider kết nối, Hạn mức cấp phát (`StorageQuotaBytes`) và Dung lượng thực tế đã dùng (`StorageUsedBytes`).

#### UC-SA-17: Truy vấn Nhật ký Kiểm toán Toàn Hệ thống (GetAuditLogs)
- **EARS-38 (Event-driven):** `WHEN` SystemAdmin tìm kiếm Audit Logs theo các tiêu chí lọc (`TenantId`, `Action`, `SearchTerm`, `PageNumber`, `PageSize`), `THEN` hệ thống `SHALL` truy vấn dữ liệu từ bảng `AuditLogs` xuyên suốt toàn bộ các Tenant, nạp thông tin Actor (User/System) và trả về danh sách phân trang `PaginatedList<AuditLogDto>` kèm trạng thái phân loại (`SUCCESS`, `FAILED`, `WARNING`).
- **EARS-39 (State-driven):** `WHILE` truy vấn Audit Logs với khối lượng dữ liệu lớn, hệ thống `SHALL` thực thi bằng cơ chế `AsNoTracking()` và `AsSplitQuery()` để tối ưu hóa hiệu năng và bộ nhớ.

---

## 4. Non-Functional Requirements (NFR)

1. **NFR-SEC-01 (Mã hóa Cấu hình Bí mật):** Toàn bộ cấu hình nhạy cảm (`credentials`, API keys, `refresh_token`, Service Account JSON) của các Storage Provider phải được mã hóa chuẩn **AES-256 CBC/GCM** trước khi lưu vào CSDL. Không bao giờ xuất khóa giải mã ra ngoài tầng Infrastructure hoặc API response.
2. **NFR-SEC-02 (Bảo vệ Token & Phiên làm việc):** Mọi hành động làm thay đổi bảo mật của tài khoản (Khóa tài khoản, Đổi mật khẩu, Gán lại vai trò) phải làm mất hiệu lực toàn bộ `RefreshToken` ngay lập tức và tăng `AuthorizationVersion` để buộc làm mới Access Token trong tối đa 1 chu kỳ request.
3. **NFR-PERF-01 (Thời gian Phản hồi):** Thời gian xử lý cho các truy vấn dữ liệu phân trang (`GetUsersWithPagination`, `GetAuditLogs`) phải hoàn tất trong **< 300ms** đối với tập dữ liệu 100,000 bản ghi (được hỗ trợ bởi Index trên `CreatedAt`, `TenantId`, `UserId`).
4. **NFR-PERF-02 (Thời gian Timeout Kiểm thử Đám mây):** Thao tác kiểm thử kết nối (`TestStorageProviderConnection`) với Cloud bên thứ 3 (Google Drive, Microsoft Graph) phải có giới hạn Timeout tối đa **15 giây** để chống nghẽn thread pool.
5. **NFR-RELIABILITY-01 (Toàn vẹn Dung lượng Đa tổ chức):** Tổng dung lượng cấp phát cho các Tenant từ một Provider (`Sum(TenantQuotaBytes)`) không bao giờ được vượt quá dung lượng thực tế khả dụng của Provider đó tại thời điểm cấp phát.

---

## 5. Data Model & Contracts

### 5.1 Các thực thể chính liên quan (Domain Entities)
- `User`: Quản lý tài khoản, mật khẩu băm, trạng thái kích hoạt, quan hệ Tenant/Department, `AuthorizationVersion`.
- `Tenant`: Quản lý tổ chức, mã định danh, `StorageProviderId`, hạn mức `StorageQuotaBytes`, `StorageUsedBytes`.
- `StorageProvider`: Quản lý cấu hình đám mây, loại (`Local`, `GoogleDrive`, `OneDrive`), chuỗi mã hóa `ConfigEncrypted`, `TotalCapacityBytes`, `UsedCapacityBytes`, `RootFolderId`, `IsActive`.
- `AuditLog`: Bản ghi nhật ký thao tác xuyên tổ chức (`Action`, `UserId`, `TenantId`, `IpAddress`, `Details`, `CreatedAt`).
- `RefreshToken`: Quản lý phiên làm việc, mã token, trạng thái thu hồi (`RevokedAt`, `ReasonRevoked`).

### 5.2 Error Contracts
| Mã lỗi / Tình huống | Kiểu phản hồi | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- | :--- |
| Trùng lặp Email | `Result.Failure` | `400 Bad Request` | `"A user with email '{email}' already exists."` |
| Người dùng không tồn tại | `Result.Failure` | `404 Not Found` | `"User with ID '{id}' was not found."` |
| Cấm vô hiệu hóa Admin duy nhất | `Result.Failure` | `400 Bad Request` | `"Cannot deactivate the only active System Administrator in the system."` |
| Cấm xóa Admin duy nhất | `Result.Failure` | `400 Bad Request` | `"Cannot delete the only System Administrator in the system."` |
| Vượt quá dung lượng Cloud | `InvalidOperationException` | `400 Bad Request` | `"Vượt quá dung lượng khả dụng của Cloud Provider! Bạn yêu cầu {x} GB nhưng Cloud chỉ còn {y} GB."` |
| Hạ quota thấp hơn dung lượng đã dùng | `InvalidOperationException` | `400 Bad Request` | `"Không thể cấp quota nhỏ hơn dung lượng mà Tenant đã sử dụng ({used} GB)."` |
| Provider bị vô hiệu hóa | `InvalidOperationException` | `400 Bad Request` | `"Cloud Provider '{name}' hiện đang bị vô hiệu hóa."` |
| Phòng ban không thuộc Tenant | `Result.Failure` | `400 Bad Request` | `"Department '{name}' does not belong to Tenant '{tenantName}'."` |

---

## 6. Acceptance Criteria (BDD Scenarios)

### Kịch bản 1: Cấp phát hạn mức cho Tenant hợp lệ
```gherkin
Scenario: Cấp phát hạn mức lưu trữ cho Tenant thành công
  Given SystemAdmin đã đăng nhập vào hệ thống
  And Cloud Provider "Google Drive Enterprise" còn trống 50 GB khả dụng
  And Tenant "Công ty ABC" đang sử dụng 10 GB
  When SystemAdmin thực hiện cấp phát hạn mức 30 GB cho Tenant "Công ty ABC"
  Then hệ thống cập nhật StorageQuotaBytes của Tenant thành 30 GB
  And hệ thống lưu thông tin StorageProviderId liên kết
  And phản hồi trả về kết quả thành công kèm thông tin hạn mức mới
```

### Kịch bản 2: Từ chối cấp phát khi vượt quá dung lượng Cloud Provider
```gherkin
Scenario: Từ chối cấp phát dung lượng vượt quá khả năng của Provider
  Given Cloud Provider "OneDrive Shared" chỉ còn trống 5 GB khả dụng
  When SystemAdmin yêu cầu cấp phát 10 GB cho Tenant "Công ty XYZ"
  Then hệ thống từ chối thực hiện giao dịch
  And hệ thống trả về thông báo lỗi: "Vượt quá dung lượng khả dụng của Cloud Provider!"
  And không có dữ liệu nào của Tenant bị thay đổi
```

### Kịch bản 3: Bảo vệ tài khoản SystemAdmin duy nhất
```gherkin
Scenario: Không cho phép vô hiệu hóa hoặc xóa SystemAdmin duy nhất
  Given hệ thống chỉ có đúng 1 tài khoản có vai trò "SystemAdmin" đang hoạt động
  When SystemAdmin thực hiện vô hiệu hóa hoặc xóa tài khoản này
  Then hệ thống chặn thao tác
  And trả về lỗi "Cannot deactivate/delete the only System Administrator in the system."
  And tài khoản vẫn duy trì trạng thái IsActive = true
```

---

## 7. Out of Scope

1. **Thanh toán & Đăng ký gói cước trực tuyến:** Việc thanh toán tự động qua cổng thanh toán (Stripe, VNPay) hoặc tự động nâng cấp gói dung lượng không thuộc phạm vi xử lý của phân hệ này.
2. **Quản lý nội dung tệp tin của Tenant:** `SystemAdmin` không được cấp giao diện hay chức năng để duyệt, đọc, hoặc tải xuống các tệp tin mật của người dùng trong các Tenant (tôn trọng quyền riêng tư dữ liệu theo thiết kế Multi-Tenancy).
3. **Phân quyền chi tiết bên trong phòng ban:** Việc tạo phân quyền chi tiết cho nhân viên thuộc phòng ban là trách nhiệm của `TenantAdmin`, không thuộc phạm vi của `SystemAdmin`.

---

## 8. Feature Lock

- **Locked scope:** Toàn bộ 17 Use Case nghiệp vụ thuộc thư mục `CleanArchCqrs.Application/Features/SystemAdmin` (Commands & Queries cho Quản lý User, Cloud Storage Provider, Tenant Quota, và Audit Logs).
- **Deferred work:** 
  - Tự động cảnh báo qua Email/Webhook khi dung lượng Cloud Provider vượt ngưỡng 90%.
  - Cơ chế tự động xoay vòng khóa bí mật OAuth2 định kỳ mà không cần can thiệp thủ công.
- **Change path:** `/sdd-update --feature=system-admin --artifact=spec --bump=<patch|minor|major> --reason="..."`
- **Lock boundary:** Feature `system-admin` only; không ảnh hưởng tới các phân hệ `Files`, `TenantAdmin`, hoặc `Auth`.

---

## 9. Adversarial Quality Pass

1. **Pre-mortem (Rủi ro tiềm ẩn):**
   - *Rủi ro:* Cấu hình OAuth2 Microsoft OneDrive bị mất Refresh Token dẫn tới toàn bộ Tenant trỏ vào Provider này bị lỗi upload.
   - *Khắc phục:* `UC-SA-11` bắt buộc kiểm tra sự tồn tại của `refresh_token` trước khi lưu vào CSDL; nếu thiếu scope `offline_access`, lập tức hủy bỏ giao dịch.
   - *Rủi ro:* Đụng độ Race condition khi hai Admin cùng cấp phát dung lượng đồng thời vượt quá giới hạn Provider.
   - *Khắc phục:* Đưa tính toán `AvailableToAllocate` vào transaction với Isolation Level phù hợp hoặc Optimistic Concurrency Control trên `StorageProvider`.
2. **Domain Walkthrough:**
   - Đã rà soát đủ 17 handlers hiện có trong source code của `CleanArchCqrs.Application/Features/SystemAdmin`.
   - Toàn bộ các nhánh thất bại và ngoại lệ đều có kịch bản EARS Unwanted tương ứng.

---

## 10. AI Recommendation và Human Final Review

### AI Recommendation
- **Methodology Profile:** `DETAILED`
- **Status:** `PENDING HUMAN REVIEW`
- **EARS Compliance:** 100% Functional Requirements tuân thủ chuẩn 5 mẫu cú pháp EARS với các cặp Happy Path / Error Path đầy đủ.
- **Clean Architecture & Security:** Tuân thủ triệt để Zero Secret Policy (không lộ khóa AES, không trả token ra API), bảo vệ SystemAdmin duy nhất, thu hồi Refresh Token chủ động.
- **Đề xuất tiếp theo:** Human reviewer kiểm tra và phê duyệt bản đặc tả trước khi tiến hành lập kế hoạch chi tiết (`/sdd-plan`).

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho phân hệ SystemAdmin (17 Use Cases).
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED in the persisted review block.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
- **Reviewed At:**
- **Notes / Adjustments:**
