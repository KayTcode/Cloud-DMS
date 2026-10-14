# SPEC: Preview File (Xem trước Tệp tin)

- **Feature Slug:** `preview-file`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Tính năng trải nghiệm người dùng tương tác cao, hỗ trợ phân loại MIME type và hiển thị an toàn trực tiếp trên trình duyệt.
  - Risk Posture: `LOW`
  - Review Route: `Frontend Architecture Review`

---

## 1. Context & Goal
Cho phép người dùng (`Employee` / `User`) nhấp đúp vào một tệp tin hoặc chọn hành động "Xem trước" từ menu để mở [FilePreviewModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/FilePreviewModal.tsx). Modal hiển thị khung đọc nội dung phù hợp với định dạng tệp tin kèm toàn bộ thông tin chi tiết của tệp tin.

---

## 2. Actors & Roles
- **Actor:** Người dùng có quyền đọc (`file.read`) đối với tệp tin.
- **Unauthorized:** Người dùng không có quyền truy cập tệp tin bị từ chối.

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng nhấp đúp vào một tệp tin hoặc bấm nút "Xem trước", `THEN` giao diện `SHALL` mở modal xem trước hiển thị Tên, Kích thước, Loại file, Người tạo, Ngày tạo, Ngày cập nhật và Thẻ tag.
- **EARS-02 (State-driven):** `WHILE` tệp tin là định dạng hình ảnh (`image/png`, `image/jpeg`, `image/webp`, `image/svg+xml`), modal `SHALL` hiển thị ảnh phóng to ở trung tâm kèm các nút Thu nhỏ, Phóng to và Đặt lại kích thước ảnh.
- **EARS-03 (State-driven):** `WHILE` tệp tin là định dạng PDF (`application/pdf`), modal `SHALL` nhúng khung đọc PDF tài liệu trực tiếp.
- **EARS-04 (State-driven):** `WHILE` tệp tin là tệp văn bản hoặc mã nguồn (`code` / `text`), modal `SHALL` hiển thị khối mã có đánh số dòng và cuộn nội dung độc lập.
- **EARS-05 (Unwanted):** `IF` tệp tin thuộc định dạng không hỗ trợ xem trực tuyến (ví dụ tệp nén `.zip`, `.rar`, tệp nhị phân `.bin`), `THEN` modal `SHALL` hiển thị biểu tượng tệp tin và thông báo: `"Định dạng này không hỗ trợ xem trước trực tuyến. Vui lòng tải xuống để mở trên máy tính."` kèm nút "Tải xuống ngay".
- **EARS-06 (Event-driven):** `WHEN` người dùng nhấn phím `ESC` hoặc bấm nút đóng (dấu X), `THEN` modal xem trước `SHALL` đóng lại và giải phóng bộ nhớ tạm.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Modal mở tức thì trong vòng `<= 100ms`.
- **NFR-SEC-01:** Chống tấn công XSS khi render nội dung văn bản/mã nguồn bằng cơ chế escape HTML an toàn của React.

---

## 5. Acceptance Criteria (BDD)
### Kịch bản 1: Xem trước ảnh thành công
- **Given** Người dùng chọn một file ảnh `diagram.png`.
- **When** Người dùng nhấn "Xem trước".
- **Then** Modal mở ra với ảnh hiển thị sắc nét, các nút zoom hoạt động bình thường và đầy đủ thông tin metadata.

### Kịch bản 2: Xem trước file nén
- **Given** Người dùng chọn một file `backup.zip`.
- **When** Người dùng nhấn "Xem trước".
- **Then** Modal hiển thị icon zip kèm lời nhắc không thể xem trực tuyến và cung cấp nút "Tải xuống ngay".

---

## 6. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature preview-file.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
