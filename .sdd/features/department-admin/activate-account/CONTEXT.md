# SDD Phase 0: Context Discovery — Department Admin Activate Account

- **Module:** `DepartmentAdmin`
- **Sub-feature:** `activate-account`
- **Status:** `DRAFT`
- **Spec Depth Baseline:** `DETAILED`
- **Owner:** Tech Lead & Security Team (`@architecture-team`)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp luồng kích hoạt tài khoản an toàn cho Trưởng phòng ban (`DepartmentAdmin`) sau khi được Quản trị viên Tổ chức (`TenantAdmin`) bổ nhiệm qua lời mời email. Trưởng phòng nhận liên kết xác thực chứa token bảo mật, mở giao diện kích hoạt định danh rõ vai trò "Quản trị viên Phòng ban (Department Admin)", có quyền thiết lập mật khẩu cá nhân mới hoặc kích hoạt nhanh / liên kết Google Workspace, sau đó tự động đăng nhập và chuyển hướng chính xác vào Cổng Quản trị Phòng ban (`DepartmentAdminPortal`) để thực hiện các bước thiết lập tiếp theo.
- **WHY (Tại sao cần tính năng này):**
  - **Khắc phục trải nghiệm định danh sai:** Trước đây trang kích hoạt bị cố định nhãn "Tenant Admin", khiến Trưởng phòng hiểu nhầm về quyền hạn và vai trò được phân công.
  - **Khắc phục lỗi điều hướng rơi vào Swagger:** Trước đây khi người dùng kích hoạt qua Google OAuth, cơ chế callback chuyển hướng về `/` của máy chủ Backend API (cổng 5289) dẫn tới màn hình Swagger UI thay vì trở lại Cổng Quản trị Frontend SPA (cổng 3000/5173).
  - **Đảm bảo vòng đời bảo mật Token (NFR-SEC):** Token kích hoạt chỉ có hiệu lực 1 lần, giới hạn 48 giờ, xác thực đúng cặp Email + Token trước khi kích hoạt tài khoản và cấp phát JWT Token.
- **Definition of Done (DoD của Pha 0):**
  - Xác định rõ luồng xử lý từ Email Invitation -> Validate Token -> Set Password / Google SSO -> Automatic Login -> Chuyển hướng Portal.
  - Định nghĩa hợp đồng dữ liệu cho `ValidateInvitationQuery` và `AcceptInvitationCommand`.
  - Khắc phục triệt để lỗi chuyển hướng URL Backend thay vì Frontend.

---

## 2. Business Context & User Pain Points

| Vấn đề / Nỗi đau (User Pain) | Kỳ vọng giải quyết (Desired Behavior) |
| :--- | :--- |
| Nhấp vào liên kết kích hoạt hoặc đăng nhập Google lại bị nhảy sang màn hình Swagger API (`localhost:5289`) | Redirect chính xác về cổng máy khách Frontend SPA (`http://localhost:3000/auth/google/callback`) với đầy đủ JWT credentials để người dùng vào thẳng Cổng Quản trị Phòng ban. |
| Màn hình kích hoạt hiển thị sai chức danh: Trưởng phòng nhưng lại ghi "Kích hoạt Quản trị viên Tổ chức (Tenant Admin)" | API kiểm tra token trả về thông tin chi tiết (`Role = 'DepartmentAdmin'`, `DepartmentName`), Frontend hiển thị động giao diện và thông điệp chuẩn xác cho Trưởng phòng. |
| Mã kích hoạt bị lộ hoặc dùng lại nhiều lần | Token được lưu dạng SHA-256 / GUID an toàn trong `EmailVerificationTokens`, tự động thu hồi ngay khi kích hoạt lần đầu (`IsUsed = true`), hết hạn sau 48 giờ. |
| Trưởng phòng kích hoạt tài khoản xong không biết phải làm gì tiếp theo | Hệ thống tự động phát JWT Token và chuyển hướng thẳng vào màn hình Cổng Quản trị Phòng ban (`DepartmentAdminPortal`), nếu phòng ban chưa thiết lập sẽ mở wizard tự thiết lập (`SetupDepartment`). |

---

## 3. Domain Glossary (Thuật ngữ Nghiệp vụ)

- **DepartmentAdmin:** Người dùng có vai trò Quản trị viên Phòng ban trong tổ chức (`Tenant`).
- **DepartmentAdminInvitation:** Loại token kích hoạt (`TokenType = 'DepartmentAdminInvitation'`) được phát sinh khi `TenantAdmin` tạo hồ sơ Trưởng phòng.
- **Activation Link:** Đường dẫn gửi trong email: `{FrontendBaseUrl}/accept-invitation?token={Token}&email={Email}`.
- **AcceptInvitation:** Thao tác xác nhận tài khoản, cập nhật mật khẩu mới (hoặc xác thực Google), đổi `IsActive = true`, `EmailConfirmed = true` và đánh dấu `IsUsed = true`.
- **DepartmentAdminPortal:** Giao diện điều hành dành riêng cho Trưởng phòng trên Frontend SPA.
