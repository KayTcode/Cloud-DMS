# SDD Phase 0: Context Discovery — Update User Profile & Avatar (Cập nhật Hồ sơ & Ảnh đại diện)

- **Feature Slug:** `update-user-profile`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp trang quản lý thông tin cá nhân [ProfileView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/ProfileView.tsx) cho phép người dùng xem và cập nhật Họ tên, Chức danh, Số điện thoại, Địa điểm công tác, Múi giờ, Ngôn ngữ hiển thị và tải lên Ảnh đại diện mới (Avatar).
- **WHY:** Giúp thông tin nhân sự trên hệ thống luôn chuẩn xác, hiển thị hình ảnh đại diện cá nhân hóa trên thanh điều hướng và nhật ký chia sẻ tài liệu.
- **DoD Pha 0:** Định nghĩa các ràng buộc dữ liệu đầu vào (độ dài họ tên, regex số điện thoại, kích thước ảnh < 5MB).

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Thông tin nhân sự bị sai lệch (sai số điện thoại, đổi chức danh) không thể tự chỉnh sửa. | Tự cập nhật hồ sơ cá nhân và lưu vĩnh viễn vào CSDL qua `PUT /api/admin/users/{id}`. |
| Muốn đổi avatar cá nhân nhưng hệ thống chỉ hiển thị avatar chữ cái mặc định. | Hỗ trợ upload ảnh đại diện mới từ máy tính (hỗ trợ JPG, PNG, WebP). |

---

## 3. Boundaries & Scope
- **In-Scope:** Xem và cập nhật profile, upload avatar, kiểm tra dung lượng ảnh đại diện.
- **Out-of-Scope:** Đổi địa chỉ Email tài khoản (yêu cầu quy trình xác thực riêng do Admin phê duyệt); Đổi phòng ban (thuộc quyền TenantAdmin/DepartmentAdmin).

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `LOW`
- **Review Route:** `Architecture Review`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature update-user-profile.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
