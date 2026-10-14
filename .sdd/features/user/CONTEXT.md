# SDD Phase 0: Master Context Discovery — Phân hệ Cổng Người dùng (User Portal)

- **Module:** `User Portal / Employee Experience` (`user`)
- **Version:** `1.2.0`
- **Scope:** Khảo sát toàn diện phân hệ Cổng Người dùng cuối (`User` / `Employee`), kết hợp giữa việc **kế thừa phân hệ Core Files** ([`.sdd/features/files`](file:///d:/Ky%209/DoAn_2/.sdd/features/files)) và **7 Feature con đặc thù riêng của User Portal** ([create-folder](file:///d:/Ky%209/DoAn_2/.sdd/features/user/create-folder/CONTEXT.md), [preview-file](file:///d:/Ky%209/DoAn_2/.sdd/features/user/preview-file/CONTEXT.md), [toggle-star-file](file:///d:/Ky%209/DoAn_2/.sdd/features/user/toggle-star-file/CONTEXT.md), [share-file](file:///d:/Ky%209/DoAn_2/.sdd/features/user/share-file/CONTEXT.md), [get-user-storage-quota](file:///d:/Ky%209/DoAn_2/.sdd/features/user/get-user-storage-quota/CONTEXT.md), [update-user-profile](file:///d:/Ky%209/DoAn_2/.sdd/features/user/update-user-profile/CONTEXT.md), [change-user-password](file:///d:/Ky%209/DoAn_2/.sdd/features/user/change-user-password/CONTEXT.md)).
- **Status:** `DRAFT`
- **Spec Depth Baseline:** `DETAILED`
- **Decision Owner:** Tech Lead & Security Lead (`@architecture-team`)

---

## 1. Intent Packet (Mục tiêu Cốt lõi)

- **WHAT (Hệ thống cần làm gì ở góc độ người dùng/nghiệp vụ):**
  Cung cấp cổng không gian làm việc số (Digital Workspace) cho nhân viên (`Employee` / `User`) trong tổ chức (`Tenant`), loại bỏ hoàn toàn dữ liệu giả lập (Mock Data) trong `Cloud-DMS-FE/src/features/user`. Kiến trúc bao gồm 2 tầng rõ ràng:
  1. **Tầng Kế thừa Nghiệp vụ Tệp tin (Core File Subsystem):** Tái sử dụng trực tiếp các API chuẩn và đặc tả kỹ thuật từ [`.sdd/features/files/`](file:///d:/Ky%209/DoAn_2/.sdd/features/files) cho 7 nghiệp vụ: Tải lên (`upload`), Xem danh sách (`get-list`), Xóa mềm vào thùng rác (`soft-delete`), Xem danh sách rác (`trash-list`), Khôi phục (`restore`), Tải xuống (`download`), và Xóa vĩnh viễn giải phóng dung lượng (`permanent-delete`).
  2. **Tầng Tính năng Đặc thù User Portal (Dedicated User Features):** Xây dựng 7 module con phục vụ trải nghiệm người dùng cuối: Tạo thư mục & điều hướng Breadcrumbs, Xem trước tài liệu đa định dạng, Gắn dấu sao tệp tin, Chia sẻ tài liệu nội bộ, Quản lý Quota cá nhân & gửi form xin nâng hạn mức, Cập nhật thông tin cá nhân/Avatar, và Đổi mật khẩu tài khoản.

- **WHY (Tại sao phân tách kiến trúc như vậy):**
  - **Tuân thủ nguyên tắc Single Source of Truth (SSOT) & DRY:** Toàn bộ logic nghiệp vụ về tệp tin nhị phân (quét ClamAV, tính hash SHA-256, kiểm tra Quota 3 tầng, giao dịch Serializable CSDL) được định nghĩa duy nhất tại [`.sdd/features/files/`](file:///d:/Ky%209/DoAn_2/.sdd/features/files). Phân hệ `user` không sao chép lại để tránh lệch pha (Spec Drift).
  - **Tối ưu hóa module hóa:** Giữ cho phân hệ `user/` gọn gàng, chỉ tập trung vào các tính năng riêng của giao diện cổng nhân viên.

- **Definition of Done (DoD của Pha 0):**
  - Thiết lập bảng liên kết tham chiếu rõ ràng sang [`.sdd/features/files/`](file:///d:/Ky%209/DoAn_2/.sdd/features/files).
  - Phân rã 7 feature con đặc thù thành 7 thư mục con độc lập trong `.sdd/features/user/`, mỗi feature có tài liệu `CONTEXT.md` và `SPEC.md` riêng biệt.
  - Phân loại và gán disposition cho các giả định/câu hỏi mở.

---

## 2. Bảng Phân bổ Kiến trúc & Danh mục Feature

```text
.sdd/features/
├── files/                        # [CORE SUBSYSTEM] Dùng chung cho toàn hệ thống
│   ├── upload/                   # 1. Tải lên tệp tin (Single & Batch, ClamAV, SHA-256)
│   ├── get-list/                 # 2. Xem danh sách tệp tin & Tổng hợp Quota
│   ├── soft-delete/              # 3. Xóa mềm tệp tin vào Thùng rác
│   ├── trash-list/               # 4. Xem danh sách tệp tin trong Thùng rác
│   ├── restore/                  # 5. Khôi phục tệp tin từ Thùng rác
│   ├── download/                 # 6. Tải xuống tệp tin (Binary Stream trực tiếp)
│   └── permanent-delete/         # 7. Xóa vĩnh viễn tệp tin & Hoàn trả Quota
│
└── user/                         # [PORTAL SUBSYSTEM] Cổng người dùng cuối (Employee)
    ├── CONTEXT.md                # Master Context Discovery của phân hệ User
    ├── SPEC.md                   # Master Specification của phân hệ User
    ├── create-folder/            # 1. Tạo thư mục mới & Điều hướng cây thư mục Breadcrumbs
    ├── preview-file/             # 2. Xem trước tệp tin đa định dạng (Ảnh, PDF, Code, Text)
    ├── toggle-star-file/         # 3. Gắn dấu sao / Bỏ dấu sao tệp tin yêu thích
    ├── share-file/               # 4. Chia sẻ tài liệu nội bộ qua email & Phân quyền
    ├── get-user-storage-quota/   # 5. Theo dõi Quota cá nhân & Gửi đề xuất tăng dung lượng
    ├── update-user-profile/      # 6. Cập nhật hồ sơ cá nhân & Đổi ảnh đại diện Avatar
    └── change-user-password/     # 7. Đổi mật khẩu tài khoản & Quản lý bảo mật 2FA
```

---

## 3. Bản đồ Tham chiếu sang Phân hệ Core Files

Khi người dùng thực hiện các thao tác tệp tin trong giao diện [DriveView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/DriveView.tsx), hệ thống sử dụng trực tiếp các đặc tả và API từ Core Files:

| Thao tác người dùng | API Backend tương ứng | Đặc tả nghiệp vụ chuẩn (Core) |
| :--- | :--- | :--- |
| **Tải lên tệp tin (Upload)** | `POST /api/files/upload` | [features/files/upload/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/upload/SPEC.md) |
| **Duyệt danh sách file (Listing)** | `GET /api/files` | [features/files/get-list/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/get-list/SPEC.md) |
| **Xóa tệp tin (Move to Trash)** | `DELETE /api/files/{id}` | [features/files/soft-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/soft-delete/SPEC.md) |
| **Xem thùng rác (Trash List)** | `GET /api/files/trash` | [features/files/trash-list/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/trash-list/SPEC.md) |
| **Khôi phục tệp tin (Restore)** | `POST /api/files/{id}/restore` | [features/files/restore/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/restore/SPEC.md) |
| **Tải xuống tệp tin (Download)** | `GET /api/files/{id}/download` | [features/files/download/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/download/SPEC.md) |
| **Xóa vĩnh viễn (Permanent Delete)** | `DELETE /api/files/{id}/permanent` | [features/files/permanent-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/permanent-delete/SPEC.md) |

---

## 4. Danh mục 7 Feature con Đặc thù của User Portal

| STT | Feature Slug | Tên tính năng | Context | Spec |
| :---: | :--- | :--- | :---: | :---: |
| 1 | `create-folder` | Tạo thư mục mới & Điều hướng cây thư mục Breadcrumbs | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/create-folder/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/create-folder/SPEC.md) |
| 2 | `preview-file` | Xem trước tệp tin đa định dạng (Ảnh, PDF, Code, Text) | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/preview-file/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/preview-file/SPEC.md) |
| 3 | `toggle-star-file` | Gắn dấu sao / Bỏ dấu sao tệp tin yêu thích | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/toggle-star-file/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/toggle-star-file/SPEC.md) |
| 4 | `share-file` | Chia sẻ tài liệu nội bộ qua email & Phân quyền Viewer/Editor | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/share-file/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/share-file/SPEC.md) |
| 5 | `get-user-storage-quota`| Theo dõi Quota cá nhân & Gửi đề xuất xin tăng dung lượng | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/get-user-storage-quota/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/get-user-storage-quota/SPEC.md) |
| 6 | `update-user-profile` | Cập nhật hồ sơ cá nhân & Đổi ảnh đại diện Avatar | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/update-user-profile/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/update-user-profile/SPEC.md) |
| 7 | `change-user-password` | Đổi mật khẩu tài khoản & Quản lý bảo mật 2FA | [CONTEXT.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/change-user-password/CONTEXT.md) | [SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/user/change-user-password/SPEC.md) |

---

## 5. Methodology Profile

- **Spec Depth Baseline:** `DETAILED`
- **Rationale:** Phân hệ phục vụ toàn bộ người dùng cuối, kết hợp chặt chẽ với Core Storage Subsystem và bảo đảm an toàn dữ liệu cá nhân.
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`

---

## 6. Open Questions & Disposition

| STT | Câu hỏi / Vấn đề mở | Assumption / Disposition | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1 | Các tính năng upload, delete có cần viết spec trùng trong thư mục `user` không? | Không; Phân hệ `user` tái sử dụng trực tiếp các spec chuẩn trong `.sdd/features/files/` để bảo đảm nguyên tắc SSOT & DRY. | `RESOLVED` |
| 2 | Khi người dùng gửi yêu cầu tăng Quota, ai sẽ phê duyệt? | Yêu cầu gửi tới Trưởng phòng ban (`DepartmentAdmin`) duyệt qua chức năng `allocate-user-storage-quota`. | `RESOLVED` |
| 3 | Người dùng có được chia sẻ tệp tin ra ngoài tổ chức không? | Không; hệ thống giới hạn chỉ chia sẻ cho tài khoản cùng Tenant để bảo đảm an toàn dữ liệu. | `RESOLVED` |

---

## 7. Describe-back Record

- **Tóm tắt của Agent:** Phân hệ `user` hoạt động như một Cổng Trải nghiệm Người dùng (User Portal). Thay vì viết lại 7 tính năng tệp tin gây trùng lặp với `.sdd/features/files/`, phân hệ `user` tham chiếu trực tiếp tới các đặc tả cốt lõi của `files/`, đồng thời quản lý tập trung 7 tính năng con đặc thù riêng cho người dùng cuối. Cấu trúc này vừa bảo đảm tính toàn vẹn của hệ thống, vừa giúp mã nguồn và tài liệu gọn gàng, chuẩn mực.
- **Đối chiếu:** Khớp 100% với kiến trúc phân tầng Clean Architecture, các Controller tại `CleanArchCqrs.API` và giao diện `Cloud-DMS-FE/src/features/user`.

---

## 8. AI Recommendation và Human Final Review

### AI Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Đề xuất:** Phê duyệt Master Context Phase 0 cho phân hệ User Portal với kiến trúc kế thừa Core Files và 7 feature con đặc thù.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt Master CONTEXT.md cho phân hệ User Portal.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```

### Human Final Review
- **Status:** `PENDING`
