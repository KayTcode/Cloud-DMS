# SPEC: Update User Profile & Avatar (Cập nhật Hồ sơ & Ảnh đại diện)

- **Feature Slug:** `update-user-profile`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Giao diện quản lý thông tin nhân sự cá nhân hóa, kiểm tra ràng buộc dữ liệu hợp lệ và upload avatar an toàn.
  - Risk Posture: `LOW`
  - Review Route: `Architecture Review`

---

## 1. Context & Goal
Cho phép người dùng (`Employee` / `User`) truy cập tab "Hồ sơ" ([ProfileView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/ProfileView.tsx)) để xem và cập nhật các thông tin cá nhân (Họ tên, Số điện thoại, Chức danh, Địa điểm, Múi giờ, Ngôn ngữ) và thay đổi ảnh đại diện cá nhân (Avatar).

---

## 2. Actors & Roles
- **Actor:** Người dùng tự cập nhật thông tin cho chính mình (`user.read.self`).
- **Unauthorized:** Người dùng không được sửa thông tin của tài khoản khác nếu không có vai trò Admin.

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng mở tab "Hồ sơ", `THEN` hệ thống `SHALL` nạp thông tin cá nhân hiện tại từ CSDL và điền sẵn vào các trường nhập liệu.
- **EARS-02 (Event-driven):** `WHEN` người dùng chỉnh sửa Họ tên, Số điện thoại, Chức danh, Địa điểm, Múi giờ, Ngôn ngữ và nhấn "Lưu thông tin", `THEN` Frontend `SHALL` gửi request `PUT /api/admin/users/{currentUserId}` với payload cập nhật và hiển thị thông báo toast thành công.
- **EARS-03 (Event-driven):** `WHEN` người dùng chọn tệp ảnh mới từ máy tính, `THEN` hệ thống `SHALL` tải ảnh lên máy chủ, cập nhật `avatarUrl` trong hồ sơ và hiển thị ngay lập tức trên thanh điều hướng góc trên bên phải.
- **EARS-04 (Unwanted):** `IF` file ảnh đại diện tải lên vượt quá 5MB hoặc không thuộc định dạng ảnh (JPG, PNG, WebP), `THEN` giao diện `SHALL` từ chối tiếp nhận và thông báo lỗi kích thước/định dạng.
- **EARS-05 (Unwanted):** `IF` họ tên người dùng để trống, `THEN` giao diện `SHALL` chặn gửi yêu cầu và thông báo: `"Họ và tên không được để trống."`

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Chống leo quyền: Chỉ cho phép người dùng sửa đúng bản ghi `Id == currentUserId`.
- **NFR-UX-01:** Cập nhật Avatar hiển thị đồng bộ trên toàn bộ các view của ứng dụng.

---

## 5. Data Model & Error Contract
- **Endpoint:** `PUT /api/admin/users/{id}`
- **Response:** `Result<UserDto>`
- **Errors:** `InvalidData` (400), `Unauthorized` (403).

---

## 6. Acceptance Criteria (BDD)
### Kịch bản: Cập nhật thông tin thành công
- **Given** Người dùng đang ở màn hình Hồ sơ cá nhân.
- **When** Người dùng đổi số điện thoại thành `"0901234567"` và bấm "Lưu thông tin".
- **Then** Thông báo thành công xuất hiện và số điện thoại mới được lưu vào CSDL.

---

## 7. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature update-user-profile.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
