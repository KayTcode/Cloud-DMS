# SPEC: Share File (Chia sẻ Tài liệu Nội bộ)

- **Feature Slug:** `share-file`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Nghiệp vụ cấp quyền truy cập tài liệu đa người dùng, yêu cầu thực thi nghiêm ngặt nguyên tắc cô lập Tenant.
  - Risk Posture: `HIGH`
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal
Cho phép chủ sở hữu tệp tin (`Employee` / `User`) mở [ShareModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/ShareModal.tsx) để chia sẻ quyền truy cập tệp tin cho một hoặc nhiều đồng nghiệp trong cùng tổ chức qua email, với cấp độ quyền `Viewer` (chỉ xem) hoặc `Editor` (xem và chỉnh sửa).

---

## 2. Actors & Roles
- **Actor:** Chủ sở hữu tệp tin (`OwnerId == currentUserId`), `DepartmentAdmin`, `TenantAdmin`.
- **Unauthorized:** Người dùng chỉ có quyền Viewer không được phép chia sẻ tệp tin cho người khác.

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng nhấn nút "Chia sẻ" trên tệp tin, `THEN` modal [ShareModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/ShareModal.tsx) `SHALL` hiển thị với đường dẫn chia sẻ nội bộ và danh sách những người đang được cấp quyền.
- **EARS-02 (Event-driven):** `WHEN` người dùng nhấn "Sao chép liên kết", `THEN` hệ thống `SHALL` chép URL chia sẻ vào Clipboard và hiển thị thông báo toast: `"Đã sao chép liên kết chia sẻ vào bộ nhớ tạm!"`
- **EARS-03 (Event-driven):** `WHEN` người dùng nhập email của đồng nghiệp cùng tổ chức, chọn quyền (`viewer` hoặc `editor`) và nhấn gửi, `THEN` hệ thống `SHALL` gửi request `POST /api/files/{id}/share` với payload `{ emails: string[], permission: string }` và cập nhật danh sách `sharedWith`.
- **EARS-04 (Unwanted):** `IF` email người được mời không thuộc tổ chức (khác Tenant) hoặc không tồn tại trên hệ thống, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Người dùng không tồn tại trong tổ chức của bạn."`
- **EARS-05 (Unwanted):** `IF` người gọi cố gắng chia sẻ tệp tin mà mình không phải là chủ sở hữu, `THEN` hệ thống `SHALL` từ chối với lỗi HTTP 403: `"Chỉ chủ sở hữu tệp tin mới có quyền chia sẻ."`

---

## 4. Non-Functional Requirements (NFR)
- **NFR-SEC-01:** Bảo đảm tính cô lập Tenant: Tuyệt đối không cho phép chia sẻ tệp tin cho tài khoản thuộc Tenant khác.

---

## 5. Data Model & Error Contract
- **Endpoint:** `POST /api/files/{id}/share`
- **Payload:** `{ emails: string[], permission: "viewer" | "editor" }`
- **Response:** `Result<bool>`
- **Errors:** `UserNotFoundInTenant` (400), `UnauthorizedOwner` (403).

---

## 6. Acceptance Criteria (BDD)
### Kịch bản: Chia sẻ tệp tin thành công cho đồng nghiệp
- **Given** Người dùng A sở hữu tệp tin `HopDong.pdf`.
- **When** Người dùng A nhập email của Người dùng B (`dongnghiep@company.com`) với quyền `viewer` và nhấn chia sẻ.
- **Then** Hệ thống báo thành công. Khi Người dùng B đăng nhập, tệp tin xuất hiện trong tab "Được chia sẻ với tôi" của Người dùng B.

---

## 7. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature share-file.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
