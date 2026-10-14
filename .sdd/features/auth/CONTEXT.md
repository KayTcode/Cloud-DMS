# SDD Phase 0: Context Discovery — Authentication & Authorization (Auth & RBAC)

- **Module:** `Authentication & Authorization (Auth & RBAC)`
- **Feature Slug:** `auth`
- **Scope:** Toàn bộ hệ thống quản lý danh tính, xác thực (Local Login + Google SSO PKCE), vòng đời token (JWT + Refresh Token xoay vòng), kích hoạt tài khoản qua lời mời email, phân quyền động dựa trên chính sách (`PermissionPolicyProvider`), và bảo vệ phân vùng đa tổ chức (Multi-Tenant).
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Owner:** Tech Lead & Security Architect (`@architecture-team`)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp giải pháp bảo mật và kiểm soát truy cập toàn diện cho hệ thống Quản lý Tài liệu Doanh nghiệp (Cloud-DMS):
  1. Cho phép người dùng đăng nhập bằng tài khoản Email/Mật khẩu hoặc thông qua Google Workspace / Gmail với chuẩn bảo mật PKCE.
  2. Quản lý vòng đời phiên làm việc an toàn thông qua JWT Access Token ngắn hạn (15 phút) và Refresh Token dài hạn (7 ngày, băm SHA-256 trong CSDL, cơ chế xoay vòng token khi cấp mới).
  3. Quản lý quy trình kích hoạt tài khoản mới qua thư mời email (`EmailVerificationTokens`, giới hạn 48 giờ, dùng một lần duy nhất).
  4. Triển khai mô hình phân quyền kết hợp **RBAC** (theo Role) và **PBAC** (theo Permission) linh hoạt: Tự động phân giải quyền hạn qua `PermissionPolicyProvider`, cấp quyền bypass tối cao cho `SystemAdmin`, và ép buộc khóa toàn bộ API bằng chính sách mặc định (`FallbackPolicy`).
  5. Đảm bảo an toàn phân vùng dữ liệu đa tổ chức (`TenantId`) và phòng ban (`DepartmentId`) dựa trên Claims gắn kèm trong Token.
- **WHY (Tại sao cần tính năng này):**
  - **Bảo vệ dữ liệu tài sản số doanh nghiệp:** Tài liệu doanh nghiệp là dữ liệu tối mật. Mọi truy cập vào tài liệu, thư mục và quản trị bắt buộc phải được định danh và thẩm định quyền hạn chính xác.
  - **Trải nghiệm người dùng liền mạch và an toàn:** Cho phép nhân viên đăng nhập một chạm qua Google Workspace SSO, tự động duy trì phiên làm việc bằng Refresh Token ngầm mà không làm gián đoạn công việc.
  - **Ngăn chặn rò rỉ thông tin nhạy cảm:** Triệt tiêu hoàn toàn rủi ro lộ token qua URL logs/history bằng cơ chế OAuth URL Fragment (`#`), bảo vệ chống tấn công brute-force bằng Rate Limiting 30 reqs/min, và băm toàn bộ mật khẩu/refresh token trong cơ sở dữ liệu.
  - **Khả năng mở rộng phân quyền (Extensibility):** Cơ chế `PermissionPolicyProvider` giúp lập trình viên tạo mới API và gắn quyền `[Authorize(Policy = "...")]` bất kỳ lúc nào mà không phải sửa mã nguồn cấu hình tĩnh của `Program.cs`.
- **Definition of Done (DoD của Pha 0):**
  - Làm rõ toàn bộ các luồng xác thực (Login, Google SSO, Accept Invitation, Refresh Token, Logout).
  - Định nghĩa rõ cấu trúc Claims trong JWT (`sub`, `role`, `tenantId`, `departmentId`, danh sách `permission`).
  - Xác lập ranh giới phân quyền rõ ràng giữa 5 vai trò người dùng trong hệ thống.
  - Khắc phục các lỗ hổng bảo mật cấp cao (lộ token qua URL, rate limiting vô hiệu, thiếu lưu refresh token).
- **Decision Owner:** Tech Lead & Security Architect (`@architecture-team`).

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| **Rò rỉ chìa khóa bảo mật trên URL sau khi đăng nhập Google** | Chuyển toàn bộ dữ liệu trả về sang URL Fragment (`#accessToken=...`) và Cookie `HttpOnly`. Phía Frontend lập tức gọi `replaceState` xóa sạch dấu vết khỏi thanh địa chỉ và lịch sử duyệt web. |
| **Nguy cơ bị dò quét mật khẩu và token ồ ạt (Brute-force attack)** | Áp dụng chính sách Rate Limiter partition `"auth"` với giới hạn tối đa 30 requests / 1 phút / IP, chặn đứng các cuộc tấn công vét cạn. |
| **Refresh Token bị lộ hoặc dùng lại nhiều lần để duy trì phiên trái phép** | Mọi Refresh Token được băm SHA-256 trước khi lưu vào CSDL. Khi cấp token mới, token cũ bị thu hồi ngay lập tức (Refresh Token Rotation). |
| **Lập trình viên quên gắn `[Authorize]` khiến API mới bị mở công khai** | Thiết lập `FallbackPolicy` toàn ứng dụng: Mọi endpoint mặc định đều yêu cầu đăng nhập, trừ khi có cờ `[AllowAnonymous]` rõ ràng. |
| **Cấu hình Policy phân quyền cồng kềnh trong mã nguồn API** | `PermissionPolicyProvider` tự động phân giải bất kỳ quyền hạn nào được khai báo trong `[Authorize(Policy = "...")]` theo Claims của JWT token. |
| **Người dùng Tenant này có thể đọc trộm dữ liệu của Tenant khác** | Token JWT mang Claim `tenantId` và `departmentId`. Tầng Application sử dụng `CurrentUserService` ép buộc lọc dữ liệu theo Tenant ID ở mọi truy vấn. |
| **Trưởng phòng / Nhân viên kích hoạt tài khoản bị hiển thị sai vai trò** | API kiểm tra token trả về đầy đủ vai trò (`Role`) và phòng ban (`DepartmentName`), giao diện hiển thị chuẩn xác danh xưng người dùng. |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **JWT (JSON Web Token):** Chuỗi mã hóa Base64URL gồm Header, Payload (Claims) và Chữ ký số (HMAC-SHA256) dùng làm Access Token ngắn hạn (15 phút).
- **Refresh Token:** Mã định danh ngẫu nhiên (UUID) có thời hạn 7 ngày, lưu dạng băm SHA-256 trong bảng `RefreshTokens`, dùng để xin cấp Access Token mới mà không cần người dùng nhập lại mật khẩu.
- **RBAC (Role-Based Access Control):** Mô hình phân quyền dựa trên vai trò: `SystemAdmin`, `TenantAdmin`, `DepartmentAdmin`, `Manager`, `Employee`.
- **PBAC (Permission-Based Access Control):** Mô hình phân quyền chi tiết dựa trên từng quyền hạn nghiệp vụ cụ thể (ví dụ: `file.upload`, `user.manage`).
- **PermissionPolicyProvider:** Bộ cung cấp chính sách ủy quyền động của ASP.NET Core, chuyển đổi tên permission thành Authorization Policy runtime.
- **FallbackPolicy:** Chính sách bảo vệ mặc định yêu cầu người dùng phải xác thực đối với toàn bộ các controller/endpoint trong hệ thống.
- **PKCE (Proof Key for Code Exchange):** Chuẩn bảo mật OAuth 2.0 (RFC 7636) sử dụng `code_verifier` và `code_challenge` (S256) chống đánh cắp mã ủy quyền trên môi trường Client SPA.
- **Single-Use Invitation Token:** Token mời tham gia hệ thống, lưu trong bảng `EmailVerificationTokens`, có hạn 48 giờ và bị vô hiệu hóa (`IsUsed = true`) ngay lần kích hoạt đầu tiên.
- **Multi-Tenant Isolation:** Cơ chế cách ly dữ liệu giữa các tổ chức doanh nghiệp trong cùng một cơ sở dữ liệu dùng chung (Shared Database, Shared Schema).

---

## 4. Boundaries & Exclusions (Ranh giới & Phạm vi Loại trừ)

### 4.1 Trong phạm vi (In-Scope)
- Đăng nhập xác thực bằng Email/Mật khẩu nội bộ.
- Đăng nhập và liên kết tài khoản Google Workspace / Gmail qua OAuth 2.0 PKCE.
- Kích hoạt tài khoản qua đường dẫn lời mời gửi trong email.
- Phát hành, xác minh và xoay vòng JWT Access Token và Refresh Token.
- Thu hồi phiên làm việc khi người dùng đăng xuất (`Logout`).
- Phân quyền động qua `PermissionPolicyProvider` và Superadmin bypass.
- Phòng thủ tấn công bằng Rate Limiter và bảo vệ Cookie `HttpOnly, Secure`.
- Cung cấp danh tính người dùng (`UserId`, `TenantId`, `DepartmentId`, `Role`) cho các tầng ứng dụng qua `CurrentUserService`.

### 4.2 Phạm vi loại trừ (Out-of-Scope)
- Xác thực 2 yếu tố qua mã OTP SMS hoặc phần mềm Authenticator (TOTP 2FA) — sẽ được phân bổ trong sprint tính năng bảo mật nâng cao.
- Tích hợp đăng nhập qua Microsoft Azure AD hoặc chuẩn SAML 2.0 SSO — nằm ngoài phạm vi sprint hiện tại.
- Quản lý khôi phục mật khẩu qua câu hỏi bảo mật.

---

## 5. Methodology Profile

- **Depth:** `DETAILED`
- **Rationale:** Đây là cổng bảo vệ cốt lõi của toàn bộ hệ sinh thái Cloud-DMS, quản lý danh tính người dùng, bảo mật khóa phiên và phân quyền dữ liệu. Mọi sai sót đều có thể dẫn đến rò rỉ dữ liệu doanh nghiệp hoặc leo thang đặc quyền (Privilege Escalation).
- **Risk Posture:** `HIGH`
- **High-risk Review Route:** `Architecture & Security Review Mandatory`
- **Unresolved-decision Owner:** Tech Lead & Security Architect (`@architecture-team`).

---

## 6. Describe-Back Record (Ghi nhận Thấu hiểu Bài toán)

- **WHAT understood:** Hệ thống cung cấp cơ chế đăng nhập kép (Local + Google SSO), quản lý phiên làm việc bằng JWT/Refresh Token xoay vòng, hỗ trợ kích hoạt tài khoản an toàn qua email, và kiểm soát quyền truy cập chặt chẽ qua cơ chế động kết hợp RBAC và PBAC với sự cô lập tuyệt đối theo Tenant/Phòng ban.
- **WHY understood:** Bảo vệ tài liệu doanh nghiệp, chống truy cập trái phép, ngăn chặn các hình thức tấn công dò mật khẩu và rò rỉ token, tạo nền tảng bảo mật vững chắc cho toàn bộ các module nghiệp vụ khác phát triển bên trên.
- **Definition of Done understood:** Tất cả các endpoint trong `AuthController` hoạt động đúng đặc tả, token được mã hóa an toàn, chính sách phân quyền động kiểm soát được 100% các endpoint nghiệp vụ, các kiểm tra bảo mật OWASP Top 10 đều đạt chuẩn.
- **Boundaries & Exclusions understood:** Đã phân định rõ: sprint này tập trung vào JWT, Google SSO PKCE, RBAC/PBAC động; chưa triển khai 2FA TOTP và SAML SSO.
- **Assumptions and unknowns:** Giả định môi trường triển khai thực tế sẽ cung cấp `JwtSettings:SecretKey` và Google Client Credentials thông qua biến môi trường an toàn theo chuẩn Zero Secret Policy.
- **Material questions and disposition:**
  1. *Cách thức truyền token OAuth về Frontend:* Đã chốt sử dụng URL Fragment (`#`) và Cookie `HttpOnly` kết hợp `history.replaceState` để đạt chuẩn OWASP A02/A04.
  2. *Cơ chế lưu trữ Refresh Token:* Đã chốt bắt buộc băm SHA-256 và lưu entity `RefreshToken` vào CSDL cho cả 3 luồng (Local Login, Google SSO, Accept Invitation).

---

## 7. AI Agent Recommendation

- **Status:** PENDING HUMAN REVIEW
- **Scope:** Tài liệu Context Discovery cho toàn bộ phân hệ Authentication & Authorization (Auth & RBAC).
- **Recommendation:** Phê duyệt tài liệu CONTEXT.md để làm căn cứ nền tảng thống nhất với SPEC.md đã lập.
- **Evidence:** 
  - Khảo sát mã nguồn thực tế: `CleanArchCqrs.API/Authorization/PermissionPolicyProvider.cs`, `CleanArchCqrs.API/Controllers/AuthController.cs`, `CleanArchCqrs.API/Program.cs`, `CleanArchCqrs.Infrastructure/Services/JwtTokenService.cs`.
  - Đặc tả kỹ thuật chi tiết: [.sdd/features/auth/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/auth/SPEC.md).
- **Risks and assumptions:** Giả định khóa bí mật JWT được bảo vệ nghiêm ngặt trong môi trường Production.
- **Required human decision:** Human reviewer phê duyệt tài liệu CONTEXT.md.

---

## 8. Human Final Review

- **Status:** PENDING
- **Decision:** 
- **Reviewer:** 
- **Reviewed at:** 
- **Follow-up:** 
