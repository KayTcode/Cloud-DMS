# SPEC: Department Admin Activate Account (Kích hoạt Tài khoản Trưởng phòng & Nhân viên)

- **Feature Slug:** `activate-account`
- **Module:** `DepartmentAdmin`
- **Version:** `1.1.0`
- **Status:** `APPROVED`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Luồng kích hoạt tài khoản lãnh đạo phòng ban và nhân viên qua email xác thực, định danh vai trò chính xác, hỗ trợ đặt mật khẩu và liên kết Google SSO, tự động đăng nhập vào cổng Department Admin hoặc Employee Portal.
  - Risk Posture: `HIGH` (Bảo mật tài khoản, xác thực mật khẩu, chuyển hướng OAuth, chống Token Hijacking).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Khi `TenantAdmin` bổ nhiệm một Trưởng phòng (`DepartmentAdmin`) hoặc `DepartmentAdmin` mời một nhân viên (`Employee`), hệ thống sinh `EmailVerificationToken` và gửi email chứa liên kết kích hoạt.
Người dùng nhấp vào liên kết:
1. Mở trang `/accept-invitation?token=...&email=...` trên giao diện Frontend SPA.
2. Hệ thống gọi `GET /api/auth/validate-invitation` để xác thực token và lấy thông tin hiển thị (Vai trò: `DepartmentAdmin` hoặc `Employee`, Tổ chức, Phòng ban nếu có).
3. Người dùng chọn 1 trong 3 phương thức:
   - **Tự thiết lập mật khẩu:** Điền mật khẩu mới (tối thiểu 6 ký tự) và bấm kích hoạt.
   - **Kích hoạt nhanh:** Bấm kích hoạt không cần đặt mật khẩu (hệ thống giữ mật khẩu tạm đã sinh).
   - **Kích hoạt & Đăng nhập bằng Google Workspace:** Liên kết tài khoản Gmail/Google Workspace và đăng nhập tức thì.
4. Sau khi kích hoạt thành công, hệ thống tự động lưu phiên làm việc (JWT Access Token & Refresh Token) và điều hướng thẳng vào Cổng Quản trị Phòng ban (`DepartmentAdminPortal`) đối với Trưởng phòng, loại bỏ hoàn toàn việc bị chuyển hướng nhầm sang Swagger UI.

---

## 2. Actors & Roles

- **Actor:** Người nhận lời mời bổ nhiệm (`DepartmentAdmin` hoặc `Employee`) sở hữu cặp `Token` và `Email` hợp lệ.
- **Unauthorized:**
  - Token không tồn tại, sai email, đã hết hạn (> 48 giờ) hoặc đã được sử dụng trước đó (`IsUsed = true`) -> Bị từ chối (`400 Bad Request` / `404 Not Found`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` người dùng mở trang kích hoạt với `token` và `email`, `THEN` hệ thống `SHALL` kiểm tra tính hợp lệ của token trong bảng `EmailVerificationTokens` và trả về `InvitationValidationDto` gồm `IsValid`, `Email`, `FullName`, `TenantName`, `Role`, `DepartmentName` (nếu có), và `TokenType`.
- **EARS-02 (Ubiquitous):** Hệ thống `SHALL` kiểm định tính hợp lệ của lệnh kích hoạt qua `AcceptInvitationCommandValidator`:
  - `Token` không được để trống (`NotEmpty`).
  - `Email` không được để trống và phải đúng định dạng email RFC.
  - `NewPassword` nếu được chỉ định thì phải có độ dài từ 6 đến 100 ký tự.
- **EARS-03 (Event-driven):** `WHEN` người dùng gửi `AcceptInvitationCommand` với `token`, `email` và `newPassword` hợp lệ, `THEN` hệ thống `SHALL` cập nhật mật khẩu băm (`user.ChangePassword`), đặt `User.IsActive = true`, `User.EmailConfirmed = true`, đánh dấu `tokenRecord.MarkAsUsed()`, sinh access token JWT và refresh token với vai trò tương ứng, và trả về `LoginResponseDto`.
- **EARS-04 (Optional):** `WHERE` người dùng chọn kích hoạt không đặt mật khẩu mới, hệ thống `SHALL` giữ mật khẩu ngẫu nhiên đã cấp, kích hoạt tài khoản và trả về `LoginResponseDto` đăng nhập ngay.
- **EARS-05 (Event-driven):** `WHEN` người dùng chọn kích hoạt và đăng nhập qua Google OAuth, `THEN` hệ thống `SHALL` kích hoạt tài khoản và chuyển hướng tới luồng Google Login với `returnUrl` trỏ về Cổng Frontend SPA (`{FrontendOrigin}/auth/google/callback`), tuyệt đối không được chuyển hướng về trang chủ máy chủ API (`http://localhost:5289/` - Swagger).
- **EARS-06 (Unwanted):** `IF` token không tồn tại hoặc không khớp với địa chỉ email đã đăng ký, `THEN` hệ thống `SHALL` từ chối thao tác với thông báo: `"Mã kích hoạt không tồn tại hoặc email không trùng khớp."`
- **EARS-07 (Unwanted):** `IF` token đã được sử dụng trước đó (`IsUsed = true`), `THEN` hệ thống `SHALL` thông báo: `"Mã kích hoạt này đã được sử dụng trước đó. Bạn có thể tiến hành đăng nhập."`
- **EARS-08 (Unwanted):** `IF` token đã quá thời hạn 48 giờ (`ExpiresAt < DateTime.UtcNow`), `THEN` hệ thống `SHALL` thông báo: `"Mã kích hoạt đã hết hạn (quá thời hạn 48 giờ). Vui lòng liên hệ Quản trị viên để được cấp lại."`

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Single-Use Token):** Token mời phải bị vô hiệu hóa ngay trong giao dịch kích hoạt đầu tiên (`IsUsed = true`, `UsedAt = DateTime.UtcNow`).
- **NFR-SEC-02 (Redirect Safety):** Mọi URL chuyển hướng đăng nhập Google OAuth (`returnUrl`) phải được chuẩn hóa về đúng tên miền và cổng của Frontend máy khách (Frontend Origin), không được rơi vào máy chủ API nội bộ (`localhost:5289`).
- **NFR-UX-01 (Role Clarity):** Giao diện kích hoạt phải hiển thị đúng danh xưng "Quản trị viên Phòng ban (Department Admin)" hoặc "Nhân viên Phòng ban (Employee)" và tên phòng ban (nếu có), không được hiển thị nhầm danh xưng Quản trị viên Tổ chức.

---

## 5. Data Model & Error Contract

### DTOs
```csharp
public record InvitationValidationDto(
    bool IsValid,
    string Message,
    string? Email = null,
    string? FullName = null,
    string? TenantName = null,
    string? Role = null,
    string? DepartmentName = null,
    string? TokenType = null
);

public record AcceptInvitationCommand(
    string Token,
    string Email,
    string? NewPassword = null
) : IRequest<LoginResponseDto>;
```

### Endpoints
- `GET /api/auth/validate-invitation?token={token}&email={email}` -> `200 OK` (Trả về `InvitationValidationDto`)
- `POST /api/auth/accept-invitation` -> `200 OK` (Trả về `LoginResponseDto`) / `400 BadRequest`
- `GET /api/auth/google/login?returnUrl={returnUrl}` -> `302 Redirect`
- `GET /api/auth/google/callback?code={code}&state={state}` -> `302 Redirect` tới `{FrontendOrigin}/auth/google/callback`

| Tình huống vi phạm | Phản hồi | Nội dung chi tiết |
| :--- | :--- | :--- |
| Token / Email để trống | `IsValid = false` / `400` | `"Mã kích hoạt hoặc email không được để trống."` |
| Token không tồn tại / sai email | `IsValid = false` / `404` | `"Mã kích hoạt không tồn tại hoặc email không trùng khớp."` |
| Token đã sử dụng | `IsValid = false` / `400` | `"Mã kích hoạt này đã được sử dụng trước đó. Bạn có thể tiến hành đăng nhập."` |
| Token hết hạn 48h | `IsValid = false` / `400` | `"Mã kích hoạt đã hết hạn (quá thời hạn 48 giờ). Vui lòng liên hệ Quản trị viên để được cấp lại."` |
| Mật khẩu < 6 ký tự | `400 Bad Request` | `"Mật khẩu phải có độ dài tối thiểu 6 ký tự."` |

---

## 6. Definition of Done (DoD)

- [x] API `ValidateInvitationQuery` trả về `Role`, `DepartmentName`, `TokenType` cho người dùng.
- [x] Backend Google Callback chuyển hướng chính xác về URL máy khách Frontend kèm token, không rơi vào Swagger UI `http://localhost:5289/`.
- [x] Giao diện `AcceptInvitationPage.tsx` hiển thị đúng vai trò Trưởng phòng và thông báo chuyển hướng Cổng Quản trị Phòng ban.
- [x] Đăng nhập thành công và mở đúng `DepartmentAdminPortal`.

---

## 7. Out of Scope

- Tự động thay đổi vai trò sau khi kích hoạt (vai trò đã được gán cố định lúc gửi lời mời).

---

## 8. Feature Lock

- **Locked Scope:** Logic xác thực và kích hoạt tài khoản trong `AcceptInvitationCommandHandler`, `ValidateInvitationQueryHandler` và validator.
- **Change Path:** `/sdd-update --feature=activate-account --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `activate-account` only.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `APPROVED`
- **Ghi chú:** Bản đặc tả đã được cập nhật v1.1.0 khớp với mã nguồn xử lý token, validator và quy trình kích hoạt tài khoản lãnh đạo phòng ban / nhân viên.

### Human Final Review
- **Status:** `APPROVED`
- **Reviewer:** Authorized Human Reviewer
- **Decision:** APPROVED — Phê duyệt bản đặc tả activate-account v1.1.0.
- **Timestamp:** 2026-10-13T22:15:00+07:00
