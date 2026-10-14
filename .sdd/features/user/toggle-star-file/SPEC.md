# SPEC: Toggle Star File (Gắn / Bỏ Dấu Sao Tệp Tin)

- **Feature Slug:** `toggle-star-file`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Tính năng tương tác gắn nhãn nhanh, cập nhật trạng thái lạc quan và đồng bộ CSDL.
  - Risk Posture: `LOW`
  - Review Route: `Architecture Review`

---

## 1. Context & Goal
Cho phép người dùng (`Employee` / `User`) nhấp vào biểu tượng ngôi sao trên từng hàng hoặc thẻ tệp tin để chuyển đổi trạng thái yêu thích (`starred = true/false`). Khi được gắn sao, tệp tin sẽ xuất hiện trong tab bộ lọc "Có gắn dấu sao".

---

## 2. Actors & Roles
- **Actor:** Người dùng có quyền đọc (`file.read`) đối với tệp tin.
- **Unauthorized:** Người dùng chưa đăng nhập bị từ chối (`401 Unauthorized`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng nhấp vào biểu tượng ngôi sao trên một tệp tin, `THEN` Frontend `SHALL` lập tức đổi màu biểu tượng ngôi sao sang màu vàng (khi gắn sao) hoặc màu xám (khi bỏ sao) và gửi request `PUT /api/files/{id}/star` với payload `{ starred: boolean }`.
- **EARS-02 (Event-driven):** `WHEN` đổi trạng thái thành công, tệp tin `SHALL` tự động xuất hiện trong tab "Có gắn dấu sao" (nếu `starred == true`) hoặc biến mất khỏi tab này (nếu `starred == false`).
- **EARS-03 (Unwanted):** `IF` tệp tin đang nằm trong Thùng rác (`trashed == true`), `THEN` thao tác gắn dấu sao `SHALL` bị vô hiệu hóa.
- **EARS-04 (Unwanted):** `IF` request lên server bị lỗi (mất mạng hoặc 500), `THEN` Frontend `SHALL` đảo ngược lại trạng thái ngôi sao về ban đầu và hiển thị thông báo lỗi.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-UX-01:** Phản hồi giao diện tức thì dưới 10ms (Optimistic Update) trước khi API server trả kết quả.

---

## 5. Data Model & Error Contract
- **Endpoint:** `PUT /api/files/{id}/star`
- **Payload:** `{ starred: boolean }`
- **Response:** `Result<bool>`
- **Errors:** `FileNotFound` (404).

---

## 6. Acceptance Criteria (BDD)
### Kịch bản: Gắn dấu sao tệp tin
- **Given** Tệp tin đang chưa gắn sao (`starred = false`).
- **When** Người dùng nhấn biểu tượng ngôi sao.
- **Then** Ngôi sao chuyển sang màu vàng, xuất hiện thông báo toast `"Đã thêm vào mục gắn dấu sao"`, và tệp tin xuất hiện trong tab "Có gắn dấu sao".

---

## 7. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature toggle-star-file.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
