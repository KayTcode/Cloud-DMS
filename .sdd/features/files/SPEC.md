# Master Feature Specification: File Management Subsystem (SPEC.md)

- **Feature Slug:** `files`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Context & Architecture Invariants

Phân hệ `files` quản lý toàn bộ vòng đời của tệp tin trong hệ thống Cloud-DMS. Nhằm đảm bảo bảo mật và nhất quán, toàn bộ các tính năng con đều phải tuân thủ 4 nguyên tắc kiến trúc bất biến sau:

1. **Chu trình xóa an toàn 2 giai đoạn (Two-Stage Lifecycle Invariant):**
   - Xóa mềm (Soft Delete) qua [soft-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/soft-delete/SPEC.md) chỉ đánh dấu `IsDeleted = true`. Dữ liệu trên Cloud và Quota **bảo toàn nguyên vẹn**.
   - Xóa vĩnh viễn (Permanent Delete) qua [permanent-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/permanent-delete/SPEC.md) mới tiêu hủy file Cloud vật lý và hoàn trả Quota 4 tầng (Provider, Tenant, Department, User) trong giao dịch `IsolationLevel.Serializable`.
2. **Cô lập tổ chức & Phòng chống IDOR/BOLA (Multi-Tenant & Anti-IDOR Invariant):**
   - Mọi thao tác đều trích xuất `TenantId` từ token; nghiêm cấm truy cập dữ liệu xuyên Tenant.
   - Nhân viên (`Employee`) bị giới hạn phạm vi thao tác theo phòng ban (`DepartmentId`).
   - Mọi truy vấn trái phép đều trả về phản hồi bảo mật **Fail-Closed**: `"Tệp tin không tồn tại hoặc đã bị xóa."`
3. **Bảo vệ mã độc & Toàn vẹn dữ liệu (Malware & Cryptographic Invariant):**
   - Stream tải lên qua [upload/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/upload/SPEC.md) bắt buộc quét mã độc ClamAV với chính sách Fail-Closed trên môi trường Non-Development.
   - Băm SHA-256 bắt buộc để xác thực tính toàn vẹn của tệp tin.
4. **Trừu tượng hóa lưu trữ đám mây (Multi-Cloud Abstraction Invariant):**
   - Tầng ứng dụng (Application Layer) chỉ giao tiếp qua `IStorageServiceFactory` và `IStorageService`. Cấu hình Provider (`ConfigEncrypted`) được giải mã bằng AES-256 an toàn trong bộ nhớ.

---

## 2. Master Feature Matrix & Sub-Specifications

| Sub-Feature | Endpoint HTTP | Phương thức | Use Case Handler | Đặc tả chi tiết |
| :--- | :--- | :--- | :--- | :--- |
| **Upload** | `/api/files/upload` | `POST` | `UploadFileCommandHandler` | [upload/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/upload/SPEC.md) |
| **Download** | `/api/files/{id}/download` | `GET` | `DownloadFileQueryHandler` | [download/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/download/SPEC.md) |
| **Get List** | `/api/files` | `GET` | `GetTenantFilesQueryHandler` | [get-list/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/get-list/SPEC.md) |
| **Soft Delete** | `/api/files/{id}` | `DELETE` | `DeleteFileCommandHandler` | [soft-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/soft-delete/SPEC.md) |
| **Trash List** | `/api/files/trash` | `GET` | `GetTrashFilesQueryHandler` | [trash-list/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/trash-list/SPEC.md) |
| **Restore** | `/api/files/{id}/restore` | `POST` | `RestoreFileCommandHandler` | [restore/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/restore/SPEC.md) |
| **Permanent Delete** | `/api/files/{id}/permanent` | `DELETE` | `PermanentDeleteFileCommandHandler` | [permanent-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/permanent-delete/SPEC.md) |
| **Advanced Search** | `/api/files/search` | `GET` | `SearchFilesQueryHandler` | [search/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/search/SPEC.md) |
| **File Preview** | `/api/files/{id}/preview` | `POST` / `GET` | `RequestFilePreviewCommand` / `GetFilePreviewQuery` | [preview/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/preview/SPEC.md) |
| **Get File Text** | `/api/files/{id}/text` | `GET` | `GetFileTextQueryHandler` | [get-text/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/get-text/SPEC.md) |
| **Direct Read Access** | `/api/files/{id}/read-access` | `GET` | `GetFileReadAccessQueryHandler` | [read-access/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/read-access/SPEC.md) |

---

## 3. Role & Permission Matrix

| Quyền / Hành động | Policy | Employee | DepartmentAdmin | TenantAdmin | SystemAdmin |
| :--- | :--- | :---: | :---: | :---: | :---: |
| **Tải lên tệp tin (Upload)** | `file.upload` | ✅ (Trong Quota cá nhân/PB) | ✅ (Trong Quota PB) | ✅ (Trong Quota Tenant) | ✅ |
| **Tải xuống tệp tin (Download)** | `file.read` | ✅ (Chỉ file cùng PB/ACL) | ✅ (Chỉ file cùng PB) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Xem danh sách tệp tin (Get List)** | `file.read` | ✅ (Scope PB tự động) | ✅ (Scope PB tự động) | ✅ (Toàn Tenant hoặc lọc PB) | ✅ (Toàn hệ thống) |
| **Xóa mềm vào Thùng rác (Soft Delete)** | `file.delete` | ✅ (Chỉ file cùng PB) | ✅ (Chỉ file cùng PB) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Xem Thùng rác (Trash List)** | `file.read` | ✅ (Chỉ file của mình) | ✅ (Chỉ file trong PB) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Khôi phục tệp tin (Restore)** | `file.upload` | ✅ (File của mình/PB) | ✅ (File trong PB) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Xóa vĩnh viễn (Permanent Delete)** | `file.delete` | ✅ (Chỉ file sở hữu `OwnerId`) | ✅ (File trong PB) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Tìm kiếm nâng cao (Search)** | `file.read` | ✅ (Chỉ file có quyền đọc) | ✅ (Chỉ file có quyền đọc) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Xem trước tệp tin (Preview)** | `file.read` | ✅ (Chỉ file có quyền đọc) | ✅ (Chỉ file có quyền đọc) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Đọc văn bản tệp tin (Get Text)** | `file.read` | ✅ (Chỉ file có quyền đọc) | ✅ (Chỉ file có quyền đọc) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |
| **Cấp URL đọc trực tiếp (Read Access)** | `file.read` | ✅ (Chỉ file có quyền đọc) | ✅ (Chỉ file có quyền đọc) | ✅ (Toàn Tenant) | ✅ (Toàn hệ thống) |

---

## 4. Definition of Done (DoD)

- [x] Gom nhóm 11 tính năng con thành hệ thống phân mục nhất quán trong `.sdd/features/files/`.
- [x] Mỗi tính năng con đều có tài liệu `CONTEXT.md` và `SPEC.md` tuân thủ cú pháp EARS và Clean Architecture.
- [x] Báo cáo kiểm toán bảo mật chi tiết theo tiêu chuẩn OWASP được lưu trữ tại `upload/SECURITY-AUDIT.md`.

---

## 5. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** Phân hệ `files` đã được tổ chức gọn gàng trong thư mục duy nhất `.sdd/features/files/` tương tự `department-admin`, với đầy đủ tài liệu Master và các tài liệu thành phần.
- **Risk:** High (Core storage subsystem).

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
