# SDD Phase 0: Context Discovery — Get User Storage Quota & Increase Request (Quản lý Quota Cá nhân & Xin cấp thêm Dung lượng)

- **Feature Slug:** `get-user-storage-quota`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp giao diện theo dõi hạn mức lưu trữ cá nhân ([StorageView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/StorageView.tsx)) hiển thị 3 chỉ số chính: Tổng hạn mức được cấp (`Total Allocated`), Dung lượng đã sử dụng (`Used Space`), Dung lượng khả dụng (`Available Space`) cùng tỷ lệ tiêu thụ, đồng thời cung cấp form gửi yêu cầu tăng dung lượng gửi đến Trưởng phòng ban (`DepartmentAdmin`).
- **WHY:** Giúp người dùng chủ động kiểm soát dung lượng cá nhân, phát hiện sớm nguy cơ tràn đĩa và gửi đề xuất xin thêm quota trước khi bị hệ thống khóa tải lên.
- **DoD Pha 0:** Định nghĩa các ngưỡng cảnh báo dung lượng (80%, 95%) và luồng gửi yêu cầu tăng Quota.

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Không biết dung lượng tài khoản của mình còn bao nhiêu, đột ngột bị chặn upload. | Hiển thị biểu đồ đo Quota trực quan, cảnh báo màu sắc khi chạm ngưỡng 80% và 95%. |
| Khi hết dung lượng không biết liên hệ ai để xin cấp thêm. | Cung cấp nút "Yêu cầu tăng dung lượng", gửi trực tiếp số GB cần xin kèm lý do lên hệ thống quản trị. |

---

## 3. Boundaries & Scope
- **In-Scope:** Xem Quota cá nhân và tổng quan phòng ban, gửi request `POST /api/users/request-quota`.
- **Out-of-Scope:** Phê duyệt tăng Quota (chức năng thuộc về `DepartmentAdmin` qua `allocate-user-storage-quota`).

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `MEDIUM`
- **Review Route:** `Architecture Review`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature get-user-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
