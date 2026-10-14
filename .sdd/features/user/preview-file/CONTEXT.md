# SDD Phase 0: Context Discovery — Preview File (Xem trước Tệp tin)

- **Feature Slug:** `preview-file`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp modal xem trước [FilePreviewModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/FilePreviewModal.tsx) nội dung tệp tin đa định dạng: Hình ảnh (PNG, JPG, SVG, WebP kèm công cụ Zoom), Tài liệu PDF (nhúng PDF reader trực tuyến), Tệp mã nguồn và văn bản (JSON, JS, TS, HTML, CSS, SQL, TXT có đánh số dòng và tô màu cú pháp), kèm các metadata chi tiết.
- **WHY:** Giúp người dùng đọc và kiểm tra nội dung tài liệu tức thì trên trình duyệt mà không cần tốn thời gian và dung lượng tải về máy tính.
- **DoD Pha 0:** Hỗ trợ xem trước định dạng thông dụng và hiển thị thông báo chuyển hướng sang tải xuống cho các định dạng không hỗ trợ xem trực tuyến.

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Muốn xem nội dung file phải tải về máy rồi mở ứng dụng bên thứ ba. | Mở modal xem trước tức thì trên giao diện web cho ảnh, tài liệu PDF và code. |
| Với định dạng phức tạp (.zip, .exe), trình duyệt bị treo hoặc báo lỗi trắng trang. | Hiển thị icon định dạng, metadata và nút "Tải xuống ngay" thay vì cố mở trực tuyến. |

---

## 3. Boundaries & Scope
- **In-Scope:** Xem trước ảnh, PDF, text/code; hiển thị metadata (kích thước, chủ sở hữu, ngày tạo, thẻ tag); tích hợp nút tải xuống và đóng modal.
- **Out-of-Scope:** Chỉnh sửa văn bản trực tiếp trong modal xem trước (chức năng Web Editor sẽ làm ở pha sau).

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `LOW`
- **Review Route:** `Frontend Architecture Review`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature preview-file.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
