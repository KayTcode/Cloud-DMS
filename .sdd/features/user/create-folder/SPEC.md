# SPEC: Create Folder & Navigation (Tạo & Điều hướng Thư mục)

- **Feature Slug:** `create-folder`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Quản trị cấu trúc cây thư mục ảo trong phân hệ phòng ban, bảo đảm tính duy nhất của tên thư mục cùng cấp và điều hướng trực quan.
  - Risk Posture: `LOW`
  - Review Route: `Architecture Review`

---

## 1. Context & Goal
Cho phép người dùng (`Employee` / `User`) tạo thư mục con mới tại thư mục hiện tại để phân loại tài liệu. Đồng thời cung cấp thanh Breadcrumbs động hiển thị phả hệ thư mục từ Thư mục gốc đến thư mục đang mở, cho phép nhấp chọn điều hướng nhanh.

---

## 2. Actors & Roles
- **Actor:** Người dùng có quyền `file.upload` hoặc `folder.create` trong phòng ban.
- **Unauthorized:** Người dùng chưa đăng nhập bị từ chối (`401 Unauthorized`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng nhấn nút "Thư mục mới", `THEN` modal [NewFolderModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/NewFolderModal.tsx) `SHALL` hiển thị với trường nhập tên thư mục và nhãn phòng ban.
- **EARS-02 (Event-driven):** `WHEN` người dùng nhập tên thư mục hợp lệ và nhấn "Tạo thư mục", `THEN` hệ thống `SHALL` gửi request `POST /api/folders` với `name`, `department`, `parentId = currentFolderId` và thêm thư mục vào danh sách hiển thị.
- **EARS-03 (Event-driven):** `WHEN` người dùng nhấp đúp vào một thư mục, `THEN` giao diện `SHALL` cập nhật `currentFolderId`, tải danh sách tệp tin con và cập nhật đường dẫn Breadcrumbs.
- **EARS-04 (Event-driven):** `WHEN` người dùng nhấp vào một nút trên thanh Breadcrumbs, `THEN` giao diện `SHALL` quay về cấp thư mục tương ứng.
- **EARS-05 (Unwanted):** `IF` tên thư mục để trống hoặc chứa ký tự đặc biệt bị cấm (`\ / : * ? " < > |`), `THEN` giao diện `SHALL` chặn gửi yêu cầu và hiển thị lỗi: `"Tên thư mục không được chứa ký tự đặc biệt."`
- **EARS-06 (Unwanted):** `IF` tên thư mục bị trùng lặp với một thư mục khác trong cùng cấp cha, `THEN` Backend `SHALL` từ chối và trả về lỗi: `"Thư mục cùng tên đã tồn tại trong vị trí này."`

---

## 4. Non-Functional Requirements (NFR)
- **NFR-DATA-01:** Bảo đảm tính duy nhất của bộ khóa `(TenantId, ParentFolderId, Name)` trong CSDL.
- **NFR-UX-01:** Điều hướng thư mục phản hồi ngay lập tức dưới 100ms.

---

## 5. Data Model & Error Contract
- **Endpoint:** `POST /api/folders`
- **Payload:** `{ name: string, parentId?: string | null, department?: string }`
- **Response:** `Result<FolderDto>`
- **Errors:** `DuplicateFolderName` (400), `InvalidCharacters` (400).

---

## 6. Acceptance Criteria (BDD)
### Kịch bản: Tạo thư mục mới và vào bên trong
- **Given** Người dùng đang ở thư mục gốc của phòng ban.
- **When** Người dùng nhấn "Thư mục mới", nhập `"Báo cáo 2026"` và nhấn xác nhận.
- **Then** Thư mục `"Báo cáo 2026"` xuất hiện trong danh sách. Khi nhấp đúp vào, thanh Breadcrumbs hiển thị `Tài liệu > Báo cáo 2026`.

---

## 7. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature create-folder.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
