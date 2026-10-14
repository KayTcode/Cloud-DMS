# SDD Phase 0: Context Discovery — Toggle Star File (Gắn / Bỏ Dấu Sao Tệp Tin)

- **Feature Slug:** `toggle-star-file`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp tính năng đánh dấu sao (Favorite / Star) hoặc bỏ đánh dấu sao trên tệp tin, giúp người dùng gom các tài liệu thường xuyên sử dụng hoặc tài liệu quan trọng vào mục riêng.
- **WHY:** Tăng tốc độ truy cập công việc hằng ngày của nhân viên; cho phép lọc tức thì các tài liệu cần theo dõi trong tab "Có gắn dấu sao".
- **DoD Pha 0:** Định nghĩa API cập nhật cờ `isStarred` và cập nhật giao diện thời gian thực (Optimistic update).

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Mất thời gian tìm lại các tệp tin quan trọng giữa hàng nghìn tài liệu. | Bấm 1-click vào biểu tượng ngôi sao để ghim file vào mục "Có gắn dấu sao". |

---

## 3. Boundaries & Scope
- **In-Scope:** Bật/tắt trạng thái sao qua `PUT /api/files/{id}/star`, phản hồi giao diện tức thì.
- **Out-of-Scope:** Gắn nhãn màu (Color labeling) - làm ở phase sau.

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `LOW`
- **Review Route:** `Architecture Review`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature toggle-star-file.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
