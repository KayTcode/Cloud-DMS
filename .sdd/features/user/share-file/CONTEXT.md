# SDD Phase 0: Context Discovery — Share File (Chia sẻ Tài liệu Nội bộ)

- **Feature Slug:** `share-file`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp modal [ShareModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/ShareModal.tsx) cho phép người dùng chia sẻ tệp tin cho đồng nghiệp trong cùng Tenant qua email, lựa chọn quyền hạn truy cập (`Viewer` hoặc `Editor`), và sao chép đường dẫn liên kết chia sẻ nội bộ.
- **WHY:** Tăng cường khả năng cộng tác tài liệu trong doanh nghiệp, bảo đảm việc phân quyền minh bạch và ngăn chặn người ngoài tổ chức tiếp cận tài liệu nội bộ.
- **DoD Pha 0:** Xác định cơ chế cô lập Tenant (Tenant Isolation), chỉ cho phép chia sẻ với email thuộc cùng Tenant.

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Muốn gửi tài liệu cho đồng nghiệp cùng phòng phải tải về rồi gửi qua Zalo/Email bên ngoài. | Chia sẻ trực tiếp trên hệ thống; người được mời thấy ngay tài liệu trong tab "Được chia sẻ với tôi". |
| Nguy cơ chia sẻ nhầm tài liệu mật ra ngoài công ty. | Chặn chia sẻ ra ngoài: Hệ thống kiểm tra và chỉ cho phép mời các email tài khoản thuộc cùng Tenant. |

---

## 3. Boundaries & Scope
- **In-Scope:** Chia sẻ qua email tài khoản nội bộ, phân quyền `Viewer`/`Editor`, sao chép link chia sẻ.
- **Out-of-Scope:** Chia sẻ công khai ra ngoài Internet không cần đăng nhập (Public link với mật khẩu sẽ làm ở phase sau).

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `HIGH` (Bảo mật tài liệu nội bộ, ranh giới tổ chức).
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature share-file.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
