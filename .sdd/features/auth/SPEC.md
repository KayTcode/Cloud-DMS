# SPEC: Authentication & Authorization (Xác thực & Phân quyền Hệ thống)

- **Feature Slug:** `auth`
- **Module:** `Authentication & Authorization (Auth & RBAC)`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Toàn bộ hệ thống quản lý danh tính, phát hành & quay vòng JWT token, cơ chế phân quyền dựa trên chính sách (Policy/Permission-based Authorization), tích hợp Google OAuth 2.0 PKCE, bảo vệ phân vùng đa tổ chức (Multi-Tenant) và phòng ban.
  - Risk Posture: `HIGH` (Kiểm soát truy cập, bảo mật tài khoản, token vòng đời, phân quyền dữ liệu).
  - High-risk review route: `Architecture & Security Review Mandatory`
- **Owner:** Tech Lead & Security Team (`@architecture-team`)

---

## 1. Context & Goal (Bối cảnh & Mục tiêu)

Cloud-DMS là hệ thống Quản lý Tài liệu Đa Tổ chức (Enterprise Multi-Tenant Document Management System). Hệ thống đòi hỏi cơ chế kiểm soát truy cập và bảo vệ dữ liệu cực kỳ nghiêm ngặt:
1. **Xác thực (Authentication):**
   - Hỗ trợ đăng nhập truyền thống qua Email & Password (mật khẩu băm an toàn qua PBKDF2/BCrypt).
   - Hỗ trợ đăng nhập liên kết Google Workspace / Gmail qua OAuth 2.0 với chuẩn bảo mật cao nhất (PKCE S256, Nonce, State, URL Fragment `#`).
   - Cấp phát Access Token (JWT) ngắn hạn (15 phút) và Refresh Token dài hạn (7 ngày, băm SHA-256 lưu DB, xoay vòng - Rotation khi cấp mới).
   - Quản lý kích hoạt tài khoản qua lời mời email (`EmailVerificationTokens`, hạn 48 giờ, dùng 1 lần duy nhất).
2. **Phân quyền (Authorization):**
   - Kiến trúc kết hợp **RBAC (Role-Based Access Control)** và **PBAC (Permission-Based Access Control)**.
   - Cơ chế cấp quyền động qua `PermissionPolicyProvider`: Mọi endpoint gắn `[Authorize(Policy = "permission.name")]` được kiểm tra tự động mà không cần khai báo tĩnh trong `Program.cs`.
   - Siêu quyền quản trị hệ thống (`SystemAdmin` bypass mọi kiểm tra permission).
   - Cô lập dữ liệu đa tầng thông qua Claims: `tenantId` (Tổ chức) và `departmentId` (Phòng ban).
   - Chính sách mặc định toàn hệ thống (`FallbackPolicy`): Mọi endpoint đều yêu cầu đăng nhập trừ khi được đánh dấu `[AllowAnonymous]`.

---

## 2. Actors & Roles (Tác nhân & Vai trò)

### 2.1 Các vai trò người dùng (Roles Hierarchy)

| Vai trò (Role) | Cấp độ phạm vi | Mô tả & Thẩm quyền chính |
| :--- | :--- | :--- |
| **Anonymous** | Public / Khách | Chỉ được truy cập các endpoint mở: `login`, `refresh`, `validate-invitation`, `accept-invitation`, `google/login`, `google/callback`, `google/status`. |
| **SystemAdmin** | Toàn hệ thống (Global) | Quản trị viên tối cao. Sở hữu tất cả quyền hạn (`permissions.Keys.ToArray()`). Tự động bypass mọi kiểm tra permission thông qua `PermissionPolicyProvider`. |
| **TenantAdmin** | Tổ chức (`TenantId`) | Quản trị viên của một công ty/tổ chức. Quản lý toàn bộ người dùng, phòng ban, phân bổ dung lượng (Storage Quota), phân quyền, xem log kiểm toán trong tổ chức. |
| **DepartmentAdmin** | Phòng ban (`DepartmentId`) | Trưởng phòng ban. Quản lý người dùng thuộc phòng ban, thiết lập phòng ban, phân bổ hạn mức cho nhân viên, quản lý tài liệu phòng ban. |
| **Manager** | Phòng ban | Cán bộ quản lý chuyên môn. Xem danh sách người dùng phòng ban, quản lý tập tin và thư mục được giao. |
| **Employee** | Cá nhân | Nhân viên thông thường. Tải lên, tải xuống, xem tập tin và thư mục trong phạm vi phòng ban và hạn mức cá nhân (Personal Quota). |

### 2.2 Danh mục Quyền hạn Chuẩn (Permissions Registry)

- **Tenant:** `tenant.read`, `tenant.manage`
- **User:** `user.read`, `user.create`, `user.update`, `user.delete`, `user.manage`
- **Role:** `role.read`, `role.manage`
- **Department:** `department.read`, `department.create`, `department.update`, `department.delete`, `department.manage`
- **File:** `file.read`, `file.upload`, `file.update`, `file.delete`, `file.share`
- **Folder:** `folder.read`, `folder.create`, `folder.update`, `folder.delete`
- **Storage:** `storage.read`, `storage.manage`
- **Quota:** `quota.department.allocate`, `quota.user.allocate`
- **Audit:** `audit.read`
- **System:** `system.manage`

---

## 3. Functional Requirements (Yêu cầu Chức năng theo EARS)

### 3.1 Nhóm Xác thực Cơ bản (Login, Refresh, Logout)

- **EARS-AUTH-01 (Event-driven - Login):** `WHEN` người dùng gửi `LoginCommand` với email và mật khẩu hợp lệ, `THEN` hệ thống `SHALL` xác thực mật khẩu băm, kiểm tra `user.IsActive == true` và `user.EmailConfirmed == true`, sinh JWT Access Token (15 phút), sinh Refresh Token (7 ngày, băm SHA-256 lưu DB) và trả về `LoginResponseDto`.
- **EARS-AUTH-02 (Unwanted - Login Failures):** `IF` email không tồn tại hoặc mật khẩu không khớp, `THEN` hệ thống `SHALL` trả về `401 Unauthorized` với thông điệp: `"Invalid email or password."` mà không tiết lộ thông tin email có tồn tại hay không.
- **EARS-AUTH-03 (Unwanted - Inactive User):** `IF` tài khoản có `IsActive == false`, `THEN` hệ thống `SHALL` từ chối với `401 Unauthorized`: `"User account is inactive."`
- **EARS-AUTH-04 (Unwanted - Unconfirmed Email):** `IF` tài khoản có `EmailConfirmed == false`, `THEN` hệ thống `SHALL` từ chối với `401 Unauthorized`: `"Email chưa được xác nhận. Vui lòng xác nhận email trước khi đăng nhập."`
- **EARS-AUTH-05 (Event-driven - Refresh Token Rotation):** `WHEN` client gửi yêu cầu `/api/auth/refresh` kèm `refreshToken` (trong body hoặc cookie `cloud_dms_refresh_token`), `THEN` hệ thống `SHALL` kiểm tra hash trong bảng `RefreshTokens`, vô hiệu hóa token cũ, sinh cặp Access Token và Refresh Token mới, cập nhật Cookie `HttpOnly` và trả về `LoginResponseDto`.
- **EARS-AUTH-06 (Unwanted - Invalid Refresh Token):** `IF` refresh token không tồn tại, đã hết hạn hoặc đã bị thu hồi (`IsActive == false`), `THEN` hệ thống `SHALL` trả về `401 Unauthorized` yêu cầu đăng nhập lại.
- **EARS-AUTH-07 (Event-driven - Logout):** `WHEN` người dùng đã xác thực gọi `POST /api/auth/logout`, `THEN` hệ thống `SHALL` thu hồi các refresh token của người dùng, xóa cookie `cloud_dms_refresh_token` và trả về thông báo đăng xuất thành công.

### 3.2 Nhóm Kích hoạt Tài khoản qua Lời mời (Invitation Lifecycle)

- **EARS-AUTH-08 (Event-driven - Validate Invitation):** `WHEN` client gọi `GET /api/auth/validate-invitation?token={token}&email={email}`, `THEN` hệ thống `SHALL` tìm bản ghi trong `EmailVerificationTokens`, xác thực email, kiểm tra `IsUsed == false` và `ExpiresAt > UtcNow`, trả về `InvitationValidationDto` chứa `IsValid`, `Role`, `TenantName`, `DepartmentName`.
- **EARS-AUTH-09 (Event-driven - Accept Invitation):** `WHEN` người dùng gửi `AcceptInvitationCommand` với token và email hợp lệ, `THEN` hệ thống `SHALL` cập nhật mật khẩu băm (nếu có), kích hoạt `user.IsActive = true`, `user.EmailConfirmed = true`, đánh dấu `token.MarkAsUsed()`, cấp phát UserStorageQuota mặc định (nếu chưa có), cấp phát Access Token và Refresh Token (băm lưu DB) để đăng nhập tức thì.
- **EARS-AUTH-10 (Unwanted - Replay Invitation):** `IF` mã token đã sử dụng trước đó (`IsUsed == true`), `THEN` hệ thống `SHALL` từ chối với thông báo: `"Mã kích hoạt này đã được sử dụng trước đó. Vui lòng tiến hành đăng nhập."`

### 3.3 Nhóm Đăng nhập & Liên kết Google OAuth 2.0 (SSO)

- **EARS-AUTH-11 (Event-driven - Build Google Login URL):** `WHEN` client gọi `GET /api/auth/google/login?returnUrl={returnUrl}`, `THEN` hệ thống `SHALL` sinh `state` (16 bytes hex), `nonce`, `code_verifier`, tính `code_challenge` (S256), chuẩn hóa `returnUrl` an toàn và lưu vào bộ đệm (10 phút), trả về URL ủy quyền của Google (`accounts.google.com/o/oauth2/v2/auth`).
- **EARS-AUTH-12 (Event-driven - Google Callback & Fragment Redirect):** `WHEN` Google chuyển hướng về `/api/auth/google/callback?code={code}&state={state}`, `THEN` hệ thống `SHALL` đổi mã lấy ID Token qua PKCE, xác thực chữ ký và `nonce`, kiểm tra miền cho phép (`AllowedDomains`), tự động kích hoạt tài khoản nếu có lời mời đang chờ, cấp phát JWT và điều hướng trình duyệt về Frontend Origin kèm thông tin đăng nhập qua URL Fragment (`#accessToken=...`) và Cookie `HttpOnly` cho refresh token.
- **EARS-AUTH-13 (Event-driven - Link Google Account):** `WHEN` người dùng đang đăng nhập gọi `POST /api/auth/google/link` với Google ID Token, `THEN` hệ thống `SHALL` xác minh token và liên kết `GoogleSubject` vào tài khoản người dùng sau khi đối chiếu trùng khớp email.

### 3.4 Nhóm Phân quyền & Kiểm soát Truy cập (Authorization Engine)

- **EARS-AUTH-14 (Ubiquitous - Fallback Policy):** Hệ thống `SHALL` áp dụng `FallbackPolicy` yêu cầu người dùng phải xác thực (`RequireAuthenticatedUser`) cho mọi API trong toàn ứng dụng, ngoại trừ các endpoint có thuộc tính `[AllowAnonymous]`.
- **EARS-AUTH-15 (Event-driven - Dynamic Permission Policy Provider):** `WHEN` một controller hoặc action được gắn `[Authorize(Policy = "{permission_name}")]`, `THEN` `PermissionPolicyProvider` `SHALL` tự động sinh chính sách yêu cầu người dùng phải:
  1. Đã đăng nhập (`RequireAuthenticatedUser`), VÀ
  2. Mang vai trò `SystemAdmin` (Superadmin bypass), HOẶC
  3. Mang claim `permission` khớp chính xác với `{permission_name}`.
- **EARS-AUTH-16 (State-driven - Multi-Tenant Isolation):** `WHILE` xử lý bất kỳ yêu cầu dữ liệu nào của Tenant/Department, hệ thống `SHALL` trích xuất `TenantId` và `DepartmentId` từ Claims của JWT token thông qua `CurrentUserService` và ép buộc điều kiện lọc ở tầng dữ liệu (Row-Level Security), ngăn chặn tuyệt đối việc người dùng truy cập trái phép dữ liệu của tổ chức khác.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Rate Limiting Protection):** Mọi endpoint thuộc nhóm Auth (`/api/auth/*`) phải được bảo vệ bởi policy `RateLimiter("auth")` với giới hạn tối đa **30 requests / phút / IP** để chống tấn công brute-force mật khẩu và quét token.
- **NFR-SEC-02 (Credential Secrecy in Redirects):** Không được truyền `refreshToken` hay `accessToken` qua URL Query Parameter (`?`). Mọi token chuyển hướng sang SPA phải sử dụng URL Fragment (`#`) và phải được client dọn sạch (`replaceState`) ngay khi nạp xong.
- **NFR-SEC-03 (Token Cryptography):**
  - Mật khẩu phải được băm bằng thuật toán an toàn (PBKDF2 / BCrypt với Salt ngẫu nhiên).
  - Refresh Token lưu trong DB phải được băm bằng SHA-256 (`TokenHash`), không bao giờ lưu token gốc dạng plaintext.
  - JWT Signature dùng thuật toán HMAC-SHA256 với Secret Key tối thiểu 256 bits (32 bytes).
- **NFR-SEC-04 (Cookie Hardening):** Cookie `cloud_dms_refresh_token` phải có các cờ bảo vệ tối đa: `HttpOnly = true`, `Secure = true`, `SameSite = SameSiteMode.Lax`, `Path = "/"`.
- **NFR-PERF-01 (Token Generation Latency):** Thời gian phát hành Access Token và xác thực phân quyền không vượt quá **50ms** trên máy chủ.

---

## 5. Data Model & Architecture Contracts

### 5.1 Cấu trúc Claims trong JWT Access Token

```json
{
  "sub": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "nameid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "role": "DepartmentAdmin",
  "tenantId": "c9b1d2e3-4567-89ab-cdef-0123456789ab",
  "departmentId": "d8a2c3e4-5678-90ab-cdef-1234567890cd",
  "permission": [
    "user.read",
    "user.create",
    "department.read",
    "file.upload",
    "file.read"
  ],
  "iss": "CleanArchCqrs",
  "aud": "CleanArchCqrs.Client",
  "exp": 1728825600
}
```

### 5.2 Sơ đồ Thực thể Cơ sở Dữ liệu (Entity Relationship)

```
[Users] 1 ──── N [UserRoles] N ──── 1 [Roles] 1 ──── N [RolePermissions] N ──── 1 [Permissions]
   │
   ├──── 1 ──── N [RefreshTokens] (TokenHash, ExpiresAt, RevokedAt, IsActive)
   │
   └──── 1 ──── N [EmailVerificationTokens] (Token, TokenType, ExpiresAt, IsUsed)
```

### 5.3 Bảng Tổng hợp Endpoints & Quyền hạn (API Authorization Matrix)

| Method | Endpoint | Authorization | Policy / Permission | Mục đích |
| :--- | :--- | :--- | :--- | :--- |
| `POST` | `/api/auth/login` | `AllowAnonymous` | N/A | Đăng nhập Email & Mật khẩu |
| `POST` | `/api/auth/refresh` | `AllowAnonymous` | N/A | Cấp lại cặp token mới |
| `POST` | `/api/auth/logout` | `Authorize` | Authenticated | Đăng xuất, hủy cookie & refresh token |
| `GET` | `/api/auth/google/status` | `AllowAnonymous` | N/A | Kiểm tra trạng thái cấu hình Google SSO |
| `GET` | `/api/auth/google/login` | `AllowAnonymous` | N/A | Bắt đầu luồng Google OAuth PKCE |
| `GET` | `/api/auth/google/callback`| `AllowAnonymous` | N/A | Xử lý Google callback, chuyển hướng SPA |
| `POST` | `/api/auth/google/link` | `Authorize` | Authenticated | Liên kết Google ID vào tài khoản hiện tại |
| `GET` | `/api/auth/validate-invitation` | `AllowAnonymous` | N/A | Kiểm tra token lời mời qua email |
| `POST` | `/api/auth/accept-invitation` | `AllowAnonymous` | N/A | Kích hoạt tài khoản, đặt mật khẩu & login |

---

## 6. Error Handling & Contract (Quy ước Mã lỗi)

| HTTP Status | Trường hợp phát sinh | Cấu trúc phản hồi lỗi |
| :--- | :--- | :--- |
| `400 Bad Request` | Dữ liệu đầu vào sai định dạng, token hết hạn, token đã sử dụng, refresh token rỗng | `{"message": "Mã kích hoạt đã hết hạn (quá 48 giờ)..."}` |
| `401 Unauthorized` | Email/mật khẩu sai, tài khoản chưa xác nhận email, tài khoản bị khóa, JWT hết hạn | `{"message": "Invalid email or password."}` |
| `403 Forbidden` | Đã đăng nhập nhưng không đủ quyền (không có permission tương ứng và không phải SystemAdmin) | `{"message": "Forbidden"}` (ASP.NET Core Default) |
| `404 Not Found` | Không tìm thấy token xác thực hoặc người dùng liên kết | `{"message": "Mã kích hoạt không tồn tại..."}` |
| `429 Too Many Requests` | Vượt quá 30 requests / phút trên endpoint Auth | `429 Too Many Requests` (RateLimiter) |

---

## 7. Acceptance Criteria (DoD - Definition of Done)

- [x] Triển khai đầy đủ các endpoint xác thực trong `AuthController.cs`.
- [x] Mã hóa mật khẩu an toàn qua `IPasswordHasher`, không lưu plaintext.
- [x] Băm SHA-256 và lưu trữ `RefreshToken` trong cơ sở dữ liệu cho cả luồng Login, Google SSO và Accept Invitation.
- [x] `PermissionPolicyProvider` tự động phân giải chính sách cấp quyền theo claim `permission` và cho phép `SystemAdmin` bypass.
- [x] Áp dụng `FallbackPolicy` yêu cầu đăng nhập toàn ứng dụng, bảo vệ chống bỏ sót attribute `[Authorize]`.
- [x] Kích hoạt `RateLimiter` middleware với partition `"auth"` chống tấn công vét cạn.
- [x] Toàn bộ luồng Google OAuth sử dụng PKCE và chuyển hướng qua URL Fragment `#` đảm bảo không rò rỉ token.
- [x] `CurrentUserService` trích xuất chính xác `UserId`, `TenantId`, `DepartmentId`, `Role` phục vụ cô lập dữ liệu.

---

## 8. Out of Scope (Phạm vi Loại trừ)

- Xác thực đa yếu tố bằng SMS / Hardware Token (MFA/2FA qua TOTP Authenticator được phân bổ trong giai đoạn sau).
- Single Sign-On qua SAML 2.0 hoặc Microsoft Azure AD (chỉ hỗ trợ Google OpenID Connect trong phiên bản này).
- Đăng nhập không cần mật khẩu qua Magic Link (chỉ dùng link cho kích hoạt tài khoản lần đầu).

---

## 9. Feature Lock

- **Locked scope:** Toàn bộ luồng xác thực (Local + Google SSO), quản lý token JWT & Refresh Token, cấp quyền RBAC + Dynamic Permission Policy, và cô lập dữ liệu Multi-Tenant đã nêu trong tài liệu này.
- **Deferred work:** 2FA TOTP, Microsoft Azure AD SSO.
- **Change path:** `/sdd-update --feature=auth --artifact=spec --bump=<patch|minor|major> --reason="..."`
- **Lock boundary:** Phạm vi xác thực & phân quyền hệ thống Cloud-DMS.

---

## 10. AI Agent Recommendation

- **Status:** PENDING HUMAN REVIEW
- **Scope:** Đặc tả toàn diện kiến trúc Xác thực & Phân quyền (Authentication & Authorization) của Cloud-DMS.
- **Recommendation:** Phê duyệt tài liệu SPEC làm chuẩn tham chiếu chính thức cho toàn bộ các module trong hệ thống.
- **Evidence:** 
  - `CleanArchCqrs.API/Authorization/PermissionPolicyProvider.cs`
  - `CleanArchCqrs.API/Controllers/AuthController.cs`
  - `CleanArchCqrs.API/Services/CurrentUserService.cs`
  - `CleanArchCqrs.Infrastructure/Services/JwtTokenService.cs`
  - `CleanArchCqrs.Infrastructure/Persistence/DbInitializer.cs`
- **Risks and assumptions:** Giả định môi trường triển khai Production sẽ cung cấp `JwtSettings:SecretKey` thông qua biến môi trường an toàn (Zero Secret Policy).
- **Required human decision:** Human reviewer phê duyệt tài liệu SPEC.md.

---

## 11. Human Final Review

- **Status:** PENDING
- **Decision:** 
- **Reviewer:** 
- **Reviewed at:** 
- **Follow-up:** 
