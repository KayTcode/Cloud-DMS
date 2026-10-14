# SDD Phase 0: Context Discovery — Create Folder & Navigation (Tạo & Điều hướng Thư mục)

- **Feature Slug:** `create-folder`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead (`@architecture-team`)

---

## 1. Intent Packet
- **WHAT:** Cung cấp tính năng tạo thư mục mới trong phòng ban thông qua [NewFolderModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/NewFolderModal.tsx) và khả năng điều hướng cây thư mục thông qua thanh đường dẫn Breadcrumbs.
- **WHY:** Giúp người dùng tổ chức, phân loại tài liệu khoa học theo từng đề án, năm tài chính hoặc nhóm công việc, tránh tình trạng tài liệu nằm ngổn ngang ở thư mục gốc.
- **DoD Pha 0:** Xác định ràng buộc tính duy nhất của tên thư mục trong cùng cấp cha (`ParentFolderId`), kiểm tra ký tự đặc biệt và điều hướng Breadcrumbs.

---

## 2. Business Context & Pain Points
| User Pain | Desired Behavior |
| :--- | :--- |
| Tất cả tài liệu đều dồn vào thư mục gốc, khó quản lý. | Cho phép tạo thư mục con lồng nhau và gắn nhãn theo phòng ban. |
| Người dùng vào sâu trong các thư mục con không biết đường quay lại. | Thanh Breadcrumbs cho phép nhấp vào bất kỳ cấp thư mục nào để quay lại ngay lập tức. |

---

## 3. Boundaries & Scope
- **In-Scope:** Tạo thư mục con với `parentId = currentFolderId`, điều hướng Breadcrumbs, kiểm tra tên thư mục hợp lệ.
- **Out-of-Scope:** Đổi tên thư mục, di chuyển thư mục (kéo thả cây thư mục sẽ làm ở phase sau).

---

## 4. Methodology Profile & Review
- **Depth:** `DETAILED`
- **Risk Posture:** `LOW`
- **Review Route:** `Architecture Review`

---

## 5. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt CONTEXT.md cho feature create-folder.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
