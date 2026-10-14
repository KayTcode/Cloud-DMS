# SDD Phase 0: Context Discovery — Change User Password & Security (Đổi Mật khẩu & Bảo mật Tài khoản)

- **Feature Slug:** `change-user-password`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp tính năng đổi mật khẩu tài khoản trực tiếp trong tab "Bảo mật" của [ProfileView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/ProfileView.tsx): Xác thực mật khẩu cũ, kiểm tra chính sách độ phức tạp mật khẩu mới, băm mật khẩu an toàn bằng BCrypt và tăng `AuthorizationVersion` để thu hồi toàn bộ token cũ.
- **WHY:** Giúp người dùng chủ động bảo vệ tài khoản, đổi mật khẩu định kỳ hoặc khi nghi ngờ lộ lọt thông tin, ngăn ngừa việc tài khoản bị chiếm đoạt.
- **DoD Pha 0:** Định nghĩa quy tắc an toàn mật khẩu (tối thiểu 8 ký tự, có chữ hoa, chữ số) và cơ chế thu hồi phiên làm việc cũ khi đổi mật khẩu.

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Hiện tại form đổi mật khẩu chỉ hiển thị toast giả lập, không đổi được mật khẩu thật. | Kết nối với API `POST /api/auth/change-password`, băm BCrypt và cập nhật CSDL. |
| Sau khi đổi mật khẩu, các thiết bị cũ bị lộ mật khẩu vẫn truy cập được. | Tăng `AuthorizationVersion` và vô hiệu hóa toàn bộ Refresh Token cũ trên các thiết bị khác. |

---

## 3. Boundaries & Scope
- **In-Scope:** Đổi mật khẩu cá nhân, xác thực mật khẩu cũ, xem trạng thái 2FA và liên kết Google SSO.
- **Out-of-Scope:** Đặt lại mật khẩu cho người khác không cần mật khẩu cũ (thuộc quyền `SystemAdmin` qua `reset-user-password`).

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `HIGH` (Bảo mật tài khoản, xác thực danh tính).
- **Review Route:** `Security Review Mandatory`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature change-user-password.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
