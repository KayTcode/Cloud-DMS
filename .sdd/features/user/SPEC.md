# Master Specification (SPEC.md) — Phân hệ Người dùng Cuối (User / Employee Portal)

- **Feature Slug:** `user`
- **Module:** `User Portal / Employee Experience`
- **Version:** `1.1.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Phân hệ cốt lõi phục vụ mọi nhân viên cuối (`Employee`/`User`), tích hợp luồng xử lý tệp tin nhị phân trực tiếp (Upload Stream, Binary Download), bảo mật mã độc (ClamAV Fail-Closed), toàn vẹn dữ liệu (SHA-256), tương tác lưu trữ đám mây đa nền tảng (AWS S3, Azure Blob, Google Drive, Local/MinIO), bảo đảm giao dịch CSDL `Serializable` khi phân bổ Quota, và cơ chế xác thực phân quyền 3 tầng chống IDOR/BOLA.
  - Risk Posture: `HIGH` (An ninh tệp tin nhị phân, toàn vẹn Quota 3 tầng, quyền riêng tư và phân quyền chống IDOR).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

### 1.1 Bối cảnh (Context)
Trong hệ sinh thái Cloud-DMS, người dùng thông thường (`Employee` / `User`) tương tác hằng ngày với hệ thống thông qua Cổng Người dùng (`Cloud-DMS-FE/src/features/user/UserPortal.tsx`). 

Hiện tại, toàn bộ giao diện của phân hệ người dùng đang chạy dữ liệu tĩnh từ `mockData.ts`. Các thao tác như tải file, xóa, đổi mật khẩu hay xem dung lượng chỉ cập nhật state in-memory trong trình duyệt.

Trong khi đó, tầng Backend đã trang bị đầy đủ các API chuẩn Clean Architecture & CQRS tại:
- [FilesController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/FilesController.cs) (Upload, GetList, Download, SoftDelete, TrashList, Restore, PermanentDelete).
- [AuthController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/AuthController.cs) (Login, Refresh, Profile, ChangePassword).
- [UsersController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/Admin/UsersController.cs) (Update Profile, Avatar).

### 1.2 Mục tiêu (Goal)
- Định nghĩa chi tiết và toàn diện tất cả các tính năng người dùng được phép làm trong hệ thống theo chuẩn EARS.
- Thay thế hoàn toàn dữ liệu giả lập trong `Cloud-DMS-FE` bằng dữ liệu thực tế từ REST API Backend.
- Đảm bảo vòng đời tài liệu khép kín: Tải lên an toàn → Tổ chức thư mục & Gắn sao → Xem trước & Tải xuống → Chia sẻ nội bộ → Xóa mềm vào Thùng rác → Khôi phục hoặc Xóa vĩnh viễn giải phóng Quota.
- Cung cấp công cụ theo dõi hạn mức Quota cá nhân trực quan và quy trình gửi yêu cầu nâng hạn mức tới Trưởng phòng ban.
- Hoàn thiện trang quản lý hồ sơ cá nhân, đổi mật khẩu và nhật ký kiểm toán hoạt động của người dùng.

---

## 2. Actors & Roles

| Actor / Role | Quyền hạn (Permissions) | Trách nhiệm và Phạm vi trong User Portal |
| :--- | :--- | :--- |
| **Employee (User)** | `file.upload`, `file.read`, `file.delete`, `user.read.self` | Người dùng cuối thông thường. Sở hữu tài liệu cá nhân, tải lên tệp tin trong hạn mức Quota được cấp, xem trước, tải xuống, tổ chức thư mục, chia sẻ nội bộ, quản lý thùng rác cá nhân, cập nhật hồ sơ và gửi yêu cầu nâng Quota. |
| **DepartmentAdmin** | `file.*`, `storage.manage`, `department.*` | Trưởng phòng ban. Có mọi quyền của Employee, đồng thời có quyền quản lý toàn bộ tệp tin thuộc phòng ban, duyệt yêu cầu tăng Quota và phân bổ dung lượng cho nhân viên. |
| **TenantAdmin** | Toàn quyền cấp Tenant | Quản trị viên doanh nghiệp. Quản lý tổng tài nguyên lưu trữ và giám sát toàn bộ hoạt động trong tổ chức. |
| **SystemAdmin** | Toàn quyền hệ thống | Quản trị viên cao nhất, xử lý sự cố hạ tầng và Storage Provider. |
| **Unauthorized** | Không có quyền (`401` / `403`) | Bị từ chối ngay lập tức khi chưa đăng nhập hoặc truy cập dữ liệu ngoài phạm vi cho phép. |

---

## 3. Functional Architecture & Subsystems Mapping

Kiến trúc nghiệp vụ của Cổng Người dùng (`User Portal`) được phân tách thành 2 tầng rõ ràng:

### 3.1 Tầng Kế thừa Nghiệp vụ Tệp tin Cốt lõi (Core Files Subsystem)
Phân hệ `user` **không tự định nghĩa lại** mà kế thừa và tham chiếu trực tiếp 7 nghiệp vụ tệp tin cốt lõi từ phân hệ [`.sdd/features/files/`](file:///d:/Ky%209/DoAn_2/.sdd/features/files):

| STT | Thao tác người dùng | API Backend | Đặc tả chuẩn kế thừa |
| :---: | :--- | :--- | :--- |
| 1 | **Tải lên tệp tin (Upload)** | `POST /api/files/upload` | [features/files/upload/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/upload/SPEC.md) |
| 2 | **Duyệt danh sách file (Listing)** | `GET /api/files` | [features/files/get-list/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/get-list/SPEC.md) |
| 3 | **Xóa tệp tin mềm (Move to Trash)** | `DELETE /api/files/{id}` | [features/files/soft-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/soft-delete/SPEC.md) |
| 4 | **Xem Thùng rác (Trash List)** | `GET /api/files/trash` | [features/files/trash-list/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/trash-list/SPEC.md) |
| 5 | **Khôi phục tệp tin (Restore)** | `POST /api/files/{id}/restore` | [features/files/restore/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/restore/SPEC.md) |
| 6 | **Tải xuống tệp tin (Download)** | `GET /api/files/{id}/download` | [features/files/download/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/download/SPEC.md) |
| 7 | **Xóa vĩnh viễn (Permanent Delete)** | `DELETE /api/files/{id}/permanent` | [features/files/permanent-delete/SPEC.md](file:///d:/Ky%209/DoAn_2/.sdd/features/files/permanent-delete/SPEC.md) |

---

### 3.2 Tầng 7 Feature con Đặc thù của User Portal
Được tổ chức thành 7 thư mục con độc lập trong `.sdd/features/user/`, mỗi feature sở hữu `CONTEXT.md` và `SPEC.md` riêng biệt:

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

---

### TRỤ CỘT 1: QUẢN LÝ TÀI LIỆU & VÒNG ĐỜI TỆP TIN (DOCUMENTS & DRIVE)

#### UC-USER-01: Tải lên tệp tin (File Upload - Single & Batch)
- **EARS-UF-01 (Event-driven):** `WHEN` người dùng chọn hoặc kéo-thả từ 1 đến 20 tệp tin hợp lệ vào giao diện và nhấn tải lên, `THEN` hệ thống `SHALL` hiển thị tiến trình tải lên và gửi request `POST /api/files/upload` dạng `multipart/form-data` kèm `folderId` hiện tại.
- **EARS-UF-02 (Event-driven):** `WHEN` Backend nhận luồng tải lên, hệ thống `SHALL` kiểm tra Quota còn lại, quét virus qua ClamAV, tính mã băm SHA-256, chuyển tiếp stream lên Cloud Storage Provider tương ứng và lưu bản ghi `FileEntry` vào CSDL.
- **EARS-UF-03 (Event-driven):** `WHEN` tải lên hoàn tất, Backend `SHALL` trả về `MultipleFileUploadResultDto` phân tách rõ danh sách `Uploaded` và `Failed`; Frontend `SHALL` cập nhật danh sách hiển thị, tăng thanh dung lượng sử dụng và hiển thị thông báo kết quả.
- **EARS-UF-04 (Unwanted):** `IF` request tải lên không có file nào đính kèm (`files.Count == 0`), `THEN` hệ thống `SHALL` trả về lỗi HTTP 400: `"Không tìm thấy tệp tin đính kèm để tải lên."`
- **EARS-UF-05 (Unwanted):** `IF` số lượng file trong một lần gửi vượt quá 20 (`files.Count > 20`), `THEN` hệ thống `SHALL` từ chối với lỗi HTTP 400: `"Mỗi lần tải lên chỉ được phép gửi tối đa 20 tệp tin."`
- **EARS-UF-06 (Unwanted):** `IF` một file vượt quá kích thước 100 MB, `THEN` máy chủ `SHALL` từ chối tiếp nhận với mã HTTP 413: `"Kích thước tệp tin vượt quá giới hạn cho phép (tối đa 100 MB)."`
- **EARS-UF-07 (Unwanted):** `IF` file có đuôi nằm trong danh sách cấm (`.exe`, `.bat`, `.cmd`, `.sh`, `.php`, `.vbs`, `.js`, `.ps1`, `.msi`, `.dll`, `.jar`, `.com`, `.pif`, `.scr`, `.hta`), `THEN` hệ thống `SHALL` đưa file vào danh sách `Failed` với thông báo vi phạm an toàn bảo mật.
- **EARS-UF-08 (Unwanted):** `IF` dung lượng file mới vượt quá Quota cá nhân (`userQuota.UsedBytes + FileSize > userQuota.QuotaBytes`) hoặc Quota tổ chức (`tenant.StorageUsedBytes + FileSize > tenant.StorageQuotaBytes`), `THEN` hệ thống `SHALL` từ chối tải tệp tin đó và thêm vào `Failed` với thông báo dung lượng cạn kiệt.
- **EARS-UF-09 (Unwanted):** `IF` ClamAV phát hiện mã độc (`scanResult.IsInfected == true`), `THEN` hệ thống `SHALL` chặn lưu trữ lập tức, ghi `AuditLog` cảnh báo virus và thêm vào `Failed` với thông báo phát hiện mã độc.
- **EARS-UF-10 (Ubiquitous):** Hệ thống `SHALL` bảo đảm một tệp tin bị lỗi trong đợt tải lên hàng loạt không làm ảnh hưởng đến các tệp tin hợp lệ khác trong cùng đợt gửi.

---

#### UC-USER-02: Duyệt danh sách, Lọc & Tìm kiếm tệp tin (Browse, Filters & Search)
- **EARS-BS-01 (Event-driven):** `WHEN` người dùng mở tab "Tài liệu" hoặc chọn một thư mục, `THEN` hệ thống `SHALL` gọi API `GET /api/files` với tham số `folderId`, `pageNumber`, `pageSize` để nạp danh sách tệp tin và thông tin tóm tắt dung lượng.
- **EARS-BS-02 (State-driven):** `WHILE` bộ lọc được chọn là:
  - `mine`: Giao diện `SHALL` chỉ hiển thị các tệp tin có `owner.email == currentUser.email`.
  - `shared`: Giao diện `SHALL` chỉ hiển thị các tệp tin được người khác chia sẻ (`sharedWith.length > 0 && owner.email != currentUser.email`).
  - `recent`: Giao diện `SHALL` lọc các tệp tin được cập nhật hoặc tạo mới trong vòng 7 ngày gần nhất.
  - `starred`: Giao diện `SHALL` chỉ hiển thị các tệp tin có `starred == true`.
  - `trash`: Giao diện `SHALL` gọi `GET /api/files/trash` để hiển thị các tệp tin đã xóa mềm.
- **EARS-BS-03 (Event-driven):** `WHEN` người dùng nhập từ khóa vào ô tìm kiếm, `THEN` giao diện `SHALL` lọc tức thời theo Tên tệp tin, Thẻ tag (`tags`) và Tên phòng ban (`department`).
- **EARS-BS-04 (Event-driven):** `WHEN` người dùng thay đổi chế độ xem (Grid hoặc List) hoặc chọn sắp xếp (theo Tên, Ngày cập nhật, hoặc Kích thước), `THEN` giao diện `SHALL` cập nhật bố cục hiển thị tương ứng ngay lập tức.
- **EARS-BS-05 (Unwanted):** `IF` người dùng tìm kiếm từ khóa không có kết quả phù hợp, `THEN` giao diện `SHALL` hiển thị màn hình trống thân thiện: `"Không tìm thấy tài liệu phù hợp với từ khóa của bạn."`

---

#### UC-USER-03: Xem trước tệp tin đa định dạng (File Preview Modal)
- **EARS-PV-01 (Event-driven):** `WHEN` người dùng nhấp đúp vào một tệp tin hoặc chọn thao tác "Xem trước", `THEN` hệ thống `SHALL` mở modal xem trước [FilePreviewModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/FilePreviewModal.tsx) hiển thị chi tiết: Tên tệp tin, Kích thước, Định dạng, Chủ sở hữu, Ngày tạo và Ngày cập nhật.
- **EARS-PV-02 (State-driven):** `WHILE` tệp tin là dạng hình ảnh (`image/*`), modal `SHALL` hiển thị khung ảnh phóng to kèm nút Thu phóng (Zoom in/out).
- **EARS-PV-03 (State-driven):** `WHILE` tệp tin là dạng PDF (`application/pdf`), modal `SHALL` nhúng khung đọc PDF tài liệu trực tiếp.
- **EARS-PV-04 (State-driven):** `WHILE` tệp tin là tệp mã nguồn hoặc tệp văn bản thuần (code/text), modal `SHALL` hiển thị khối mã có đánh số dòng và tô màu cú pháp thích hợp.
- **EARS-PV-05 (Unwanted):** `IF` định dạng tệp tin không được hỗ trợ hiển thị trực tiếp trong trình duyệt (ví dụ .zip, .rar, .bin), `THEN` modal `SHALL` hiển thị biểu tượng tệp tin cùng thông báo: `"Định dạng này không hỗ trợ xem trước trực tuyến. Vui lòng tải xuống để mở trên máy tính."` kèm nút "Tải xuống ngay".

---

#### UC-USER-04: Tải xuống tệp tin an toàn (Direct Streaming Download)
- **EARS-DL-01 (Event-driven):** `WHEN` người dùng nhấn nút "Tải xuống" trên một tệp tin, `THEN` hệ thống FE `SHALL` kích hoạt tải về qua URL `GET /api/files/{id}/download`.
- **EARS-DL-02 (Event-driven):** `WHEN` Backend nhận yêu cầu tải xuống, hệ thống `SHALL` kiểm tra quyền truy cập (Người gọi phải cùng Tenant và là Chủ sở hữu, hoặc cùng Phòng ban, hoặc được chia sẻ tệp tin); sau đó truyền trực tiếp luồng dữ liệu nhị phân từ Cloud Storage về trình duyệt với đúng `ContentType` và tên file ban đầu.
- **EARS-DL-03 (Unwanted):** `IF` tệp tin không tồn tại hoặc đã bị xóa vĩnh viễn, `THEN` hệ thống `SHALL` trả về lỗi HTTP 404: `"Tệp tin không tồn tại hoặc đã bị xóa."`
- **EARS-DL-04 (Unwanted):** `IF` người dùng cố tình tải tệp tin của Tenant khác hoặc tệp tin riêng tư của người khác (tấn công BOLA/IDOR), `THEN` hệ thống `SHALL` từ chối với lỗi HTTP 404/403 để bảo mật Fail-Closed.

---

#### UC-USER-05: Quản lý Thư mục (Create Folder & Navigation)
- **EARS-FD-01 (Event-driven):** `WHEN` người dùng nhấn nút "Thư mục mới", nhập tên thư mục hợp lệ và xác nhận trong [NewFolderModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/NewFolderModal.tsx), `THEN` hệ thống `SHALL` gửi yêu cầu tạo thư mục với `parentId = currentFolderId` và thêm thư mục vào danh sách hiển thị.
- **EARS-FD-02 (Event-driven):** `WHEN` người dùng nhấp đúp vào một thư mục, `THEN` giao diện `SHALL` cập nhật `currentFolderId`, tải danh sách tệp tin con và cập nhật thanh điều hướng Breadcrumbs (ví dụ: `Tài liệu > Phòng Kế toán > Báo cáo 2026`).
- **EARS-FD-03 (Event-driven):** `WHEN` người dùng nhấp vào một cấp thư mục trên thanh Breadcrumbs, `THEN` giao diện `SHALL` quay ngược về cấp thư mục tương ứng.
- **EARS-FD-04 (Unwanted):** `IF` tên thư mục để trống hoặc chứa ký tự đặc biệt bị cấm (`\ / : * ? " < > |`), `THEN` giao diện `SHALL` chặn gửi yêu cầu và hiển thị thông báo: `"Tên thư mục không được chứa ký tự đặc biệt."`

---

#### UC-USER-06: Đánh dấu Yêu thích / Gắn dấu sao (Toggle Star)
- **EARS-ST-01 (Event-driven):** `WHEN` người dùng nhấn vào biểu tượng ngôi sao trên một tệp tin hoặc thư mục, `THEN` hệ thống `SHALL` đảo ngược trạng thái gắn sao (`starred = !starred`), cập nhật giao diện và gửi request cập nhật trạng thái lên API `PUT /api/files/{id}/star`.
- **EARS-ST-02 (Event-driven):** `WHEN` trạng thái đổi sang gắn sao (`starred == true`), tệp tin `SHALL` xuất hiện trong tab bộ lọc "Có gắn dấu sao". Khi bỏ dấu sao, tệp tin `SHALL` tự động rời khỏi tab này.
- **EARS-ST-03 (Unwanted):** `IF` tệp tin đang nằm trong Thùng rác (`trashed == true`), `THEN` thao tác gắn dấu sao `SHALL` bị vô hiệu hóa.

---

#### UC-USER-07: Chia sẻ Tài liệu Nội bộ & Phân quyền (File Sharing)
- **EARS-SH-01 (Event-driven):** `WHEN` người dùng nhấn nút "Chia sẻ" trên tệp tin, `THEN` modal [ShareModal.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/components/ShareModal.tsx) `SHALL` hiển thị với liên kết chia sẻ nội bộ và danh sách những người đang được cấp quyền.
- **EARS-SH-02 (Event-driven):** `WHEN` người dùng nhấn nút "Sao chép liên kết", `THEN` hệ thống `SHALL` sao chép đường dẫn chia sẻ vào Clipboard và thông báo: `"Đã sao chép liên kết chia sẻ vào bộ nhớ tạm!"`
- **EARS-SH-03 (Event-driven):** `WHEN` người dùng nhập email của đồng nghiệp cùng tổ chức, chọn quyền (`viewer` hoặc `editor`) và nhấn gửi, `THEN` hệ thống `SHALL` gửi API cấp quyền truy cập `POST /api/files/{id}/share` và cập nhật danh sách `sharedWith`.
- **EARS-SH-04 (Unwanted):** `IF` email người được mời không thuộc tổ chức (khác Tenant) hoặc không tồn tại trên hệ thống, `THEN` hệ thống `SHALL` từ chối với lỗi: `"Người dùng không tồn tại trong tổ chức của bạn."`

---

#### UC-USER-08: Xóa mềm vào Thùng rác (Soft Delete)
- **EARS-SD-01 (Event-driven):** `WHEN` người dùng nhấn nút "Xóa" trên một tệp tin do mình sở hữu và xác nhận chuyển vào thùng rác, `THEN` Frontend `SHALL` gửi request `DELETE /api/files/{id}`.
- **EARS-SD-02 (Event-driven):** `WHEN` Backend nhận lệnh `DeleteFileCommand`, hệ thống `SHALL` mở transaction CSDL `Serializable`, xác thực quyền sở hữu, cập nhật `IsDeleted = true`, `DeletedAt = DateTime.UtcNow`, ghi `AuditLog` với `Action = "MoveToTrash"` và trả về thành công.
- **EARS-SD-03 (Event-driven):** `WHEN` xóa mềm thành công, Frontend `SHALL` loại bỏ tệp tin khỏi danh sách tài liệu chính, chuyển trạng thái `trashed = true` và hiển thị thông báo: `"Đã chuyển tài liệu vào thùng rác."`
- **EARS-SD-04 (State-driven):** `WHILE` tệp tin đang ở trong Thùng rác, hệ thống `SHALL NOT` xóa file vật lý trên Cloud Storage và `SHALL NOT` giảm dung lượng Quota.
- **EARS-SD-05 (Unwanted):** `IF` người gọi là Employee cố gắng xóa file của người khác mà không phải chủ sở hữu, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."`

---

#### UC-USER-09: Khôi phục tệp tin từ Thùng rác (Restore from Trash)
- **EARS-RS-01 (Event-driven):** `WHEN` người dùng trong tab "Thùng rác" nhấn nút "Khôi phục" trên một tệp tin, `THEN` Frontend `SHALL` gửi request `POST /api/files/{id}/restore`.
- **EARS-RS-02 (Event-driven):** `WHEN` Backend nhận lệnh `RestoreFileCommand`, hệ thống `SHALL` mở transaction `Serializable`, đặt lại `IsDeleted = false`, `DeletedAt = null`, `DeletedByUserId = null`, ghi `AuditLog` với `Action = "RestoreFromTrash"` và trả về kết quả thành công.
- **EARS-RS-03 (Event-driven):** `WHEN` khôi phục thành công, Frontend `SHALL` loại bỏ tệp tin khỏi Thùng rác, đưa tệp tin trở lại danh sách tài liệu hoạt động ban đầu và thông báo: `"Đã khôi phục tài liệu thành công!"`
- **EARS-RS-04 (Unwanted):** `IF` tệp tin không nằm trong Thùng rác (`IsDeleted == false`), `THEN` hệ thống `SHALL` từ chối với thông báo: `"Tệp tin đang ở trạng thái hoạt động bình thường."`

---

#### UC-USER-10: Xóa vĩnh viễn tệp tin & Hoàn trả Quota (Permanent Delete)
- **EARS-PD-01 (Event-driven):** `WHEN` người dùng trong tab "Thùng rác" nhấn nút "Xóa vĩnh viễn" và xác nhận hộp thoại cảnh báo nguy hiểm không thể hoàn tác, `THEN` Frontend `SHALL` gửi request `DELETE /api/files/{id}/permanent`.
- **EARS-PD-02 (Event-driven):** `WHEN` Backend nhận lệnh `PermanentDeleteFileCommand`, hệ thống `SHALL` xác thực quyền sở hữu, gọi `storageService.DeleteFileAsync` để tiêu hủy file vật lý trên Cloud Storage, mở transaction `Serializable` xóa bản ghi `FileEntry` khỏi CSDL, trừ dung lượng `SizeBytes` khỏi `Tenant.StorageUsedBytes` và `UserStorageQuota.UsedBytes`, ghi `AuditLog` và trả về thành công.
- **EARS-PD-03 (Event-driven):** `WHEN` xóa vĩnh viễn thành công, Frontend `SHALL` loại bỏ tệp tin khỏi Thùng rác, cập nhật giảm dung lượng hiển thị trên thanh Quota và thông báo: `"Đã xóa vĩnh viễn tệp tin và giải phóng dung lượng lưu trữ!"`
- **EARS-PD-04 (Unwanted):** `IF` tệp tin không nằm trong Thùng rác (`!fileEntry.IsDeleted`), `THEN` hệ thống `SHALL` từ chối với lỗi: `"Tệp tin không nằm trong thùng rác hoặc đã bị xóa vĩnh viễn."`
- **EARS-PD-05 (Unwanted):** `IF` người gọi không phải chủ sở hữu tệp tin (`fileEntry.OwnerId != currentUserId`), `THEN` hệ thống `SHALL` từ chối với lỗi HTTP 403: `"Bạn không có quyền xóa vĩnh viễn tệp tin này."`

---

### TRỤ CỘT 2: THEO DÕI HẠN MỨC & QUẢN LÝ DUNG LƯỢNG (STORAGE & QUOTA)

#### UC-USER-11: Theo dõi Dung lượng Quota & Xin nâng hạn mức
- **EARS-QT-01 (Event-driven):** `WHEN` người dùng mở tab "Dung lượng" ([StorageView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/StorageView.tsx)), `THEN` hệ thống `SHALL` hiển thị 3 chỉ số chính: Tổng hạn mức được cấp (`Total Allocated`), Dung lượng đã dùng (`Used Space`), và Dung lượng còn lại (`Available Space`) kèm biểu đồ tỷ lệ sử dụng.
- **EARS-QT-02 (State-driven):** `WHILE` tỷ lệ sử dụng dung lượng cá nhân:
  - `>= 80%`: Giao diện `SHALL` hiển thị thanh trạng thái màu vàng cam cảnh báo dung lượng sắp hết.
  - `>= 95%`: Giao diện `SHALL` hiển thị thanh trạng thái màu đỏ và cảnh báo tài khoản sắp bị khóa tải lên.
- **EARS-QT-03 (Event-driven):** `WHEN` người dùng nhấn nút "Yêu cầu tăng dung lượng", nhập số GB cần xin thêm kèm lý do và nhấn gửi, `THEN` hệ thống `SHALL` gửi yêu cầu lên API `POST /api/users/request-quota` và thông báo: `"Đã gửi đề xuất tăng dung lượng tới Trưởng phòng ban thành công!"`
- **EARS-QT-04 (Unwanted):** `IF` số GB yêu cầu tăng `<= 0` hoặc lý do để trống, `THEN` giao diện `SHALL` chặn gửi yêu cầu và yêu cầu nhập thông tin hợp lệ.

---

### TRỤ CỘT 3: HỒ SƠ CÁ NHÂN & BẢO MẬT TÀI KHOẢN (PROFILE & SECURITY)

#### UC-USER-12: Quản lý Hồ sơ Cá nhân & Ảnh đại diện (Profile Update)
- **EARS-PF-01 (Event-driven):** `WHEN` người dùng mở tab "Hồ sơ" ([ProfileView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/ProfileView.tsx)), `THEN` hệ thống `SHALL` nạp thông tin cá nhân hiện tại từ API `GET /api/admin/users/{currentUserId}` hoặc thông tin từ token xác thực.
- **EARS-PF-02 (Event-driven):** `WHEN` người dùng chỉnh sửa Họ tên, Số điện thoại, Chức danh, Địa điểm, Múi giờ, Ngôn ngữ và nhấn "Lưu thông tin", `THEN` Frontend `SHALL` gửi request cập nhật `PUT /api/admin/users/{id}` và hiển thị thông báo thành công.
- **EARS-PF-03 (Event-driven):** `WHEN` người dùng chọn ảnh đại diện mới từ máy tính, `THEN` hệ thống `SHALL` tải ảnh lên máy chủ, cập nhật `avatarUrl` trong hồ sơ và hiển thị ngay ảnh mới trên góc trên thanh điều hướng.
- **EARS-PF-04 (Unwanted):** `IF` file ảnh đại diện tải lên vượt quá 5MB hoặc sai định dạng (không phải jpg, png, webp), `THEN` giao diện `SHALL` từ chối tiếp nhận và thông báo lỗi.

---

#### UC-USER-13: Đổi mật khẩu & Bảo mật tài khoản (Password & 2FA)
- **EARS-SC-01 (Event-driven):** `WHEN` người dùng nhập Mật khẩu hiện tại, Mật khẩu mới và Xác nhận mật khẩu mới rồi nhấn "Đổi mật khẩu", `THEN` hệ thống `SHALL` gửi request đến API đổi mật khẩu `POST /api/auth/change-password`.
- **EARS-SC-02 (Unwanted):** `IF` Mật khẩu mới không khớp với Xác nhận mật khẩu, `THEN` giao diện `SHALL` dừng thao tác và báo lỗi: `"Mật khẩu mới không trùng khớp."`
- **EARS-SC-03 (Unwanted):** `IF` Mật khẩu mới có độ dài dưới 8 ký tự hoặc thiếu chữ hoa/chữ số, `THEN` hệ thống `SHALL` báo lỗi: `"Mật khẩu mới phải có ít nhất 8 ký tự, bao gồm chữ hoa và chữ số."`
- **EARS-SC-04 (Unwanted):** `IF` Mật khẩu hiện tại nhập vào không chính xác, `THEN` Backend `SHALL` từ chối với lỗi HTTP 400: `"Mật khẩu hiện tại không chính xác."`
- **EARS-SC-05 (Optional):** `WHERE` người dùng muốn tăng cường bảo mật, hệ thống `SHALL` cung cấp tùy chọn bật/tắt Xác thực hai lớp (2FA) qua mã xác thực TOTP/Email.

---

#### UC-USER-14: Nhật ký Hoạt động Cá nhân (Activity Audit Log)
- **EARS-AL-01 (Event-driven):** `WHEN` người dùng chọn tab "Nhật ký hoạt động" trong trang Hồ sơ, `THEN` hệ thống `SHALL` hiển thị danh sách dòng thời gian (Timeline) các hành động cá nhân gần nhất: Thời điểm, Hành động (Upload, Delete, Restore, ChangePassword, Login), Tên tài liệu, Địa chỉ IP và Thiết bị truy cập.
- **EARS-AL-02 (State-driven):** `WHILE` xem nhật ký hoạt động, người dùng `SHALL NOT` được phép chỉnh sửa hoặc xóa bất kỳ dòng log nào để bảo đảm tính toàn vẹn kiểm toán an ninh.

---

## 4. Non-Functional Requirements (NFR)

| Tiêu chuẩn NFR | Chỉ số đo lường cụ thể | Phương thức bảo đảm |
| :--- | :--- | :--- |
| **NFR-PERF-01: Tốc độ tải danh sách** | Thời gian phản hồi `GET /api/files` `<= 300ms` (p95) cho danh mục 50 mục. | Phân trang CSDL, đánh index trên `(TenantId, FolderId, IsDeleted, OwnerId)`. |
| **NFR-PERF-02: Truyền luồng không tràn RAM** | Upload và Download sử dụng trực tiếp `Stream`, không đọc toàn bộ file vào bộ nhớ RAM. | `OpenReadStream()` kết hợp stream pipe tới Storage Service. |
| **NFR-SEC-01: Quét mã độc 100%** | 100% tệp tin tải lên phải vượt qua ClamAV scan trước khi ghi vào Cloud Storage. | Dịch vụ ClamAV TCP Stream Scan thời gian thực; Fail-Closed khi phát hiện virus. |
| **NFR-SEC-02: Phòng chống tấn công IDOR** | Tuyệt đối không cho phép truy cập, tải xuống hoặc xóa file của người khác hoặc tổ chức khác. | Kiểm tra 3 tầng: `TenantId`, `DepartmentId`, `OwnerId == currentUserId`. |
| **NFR-SEC-03: Chặn file thực thi độc hại** | Chặn 15 đuôi file nguy hiểm (`BlockedExtensions`) tại transport boundary. | Blacklist kiểm tra phần mở rộng file trước khi xử lý stream. |
| **NFR-REL-01: Toàn vẹn giao dịch Quota** | Trừ Quota khi xóa vĩnh viễn và cộng Quota khi upload thực thi trong Database Transaction `Serializable`. | EF Core Execution Strategy với `IsolationLevel.Serializable`. |
| **NFR-UX-01: Trải nghiệm người dùng mượt mà** | Toast notification phản hồi tức thì (`< 100ms`), thanh tiến trình upload trực quan. | Xử lý bất đồng bộ, cập nhật state lạc quan (Optimistic update) khi cần. |

---

## 5. Data Model & API Contracts

### 5.1 Bảng tổng hợp REST API của Phân hệ User

| Nhóm | Method | Endpoint | Quyền yêu cầu | Mô tả |
| :--- | :--- | :--- | :--- | :--- |
| **Files** | `POST` | `/api/files/upload` | `file.upload` | Tải lên 1 hoặc nhiều file (tối đa 20 file, 100MB/file). |
| **Files** | `GET` | `/api/files` | `file.read` | Lấy danh sách file đang hoạt động & tổng hợp dung lượng. |
| **Files** | `GET` | `/api/files/{id}/download` | `file.read` | Tải stream file nhị phân về máy. |
| **Files** | `DELETE`| `/api/files/{id}` | `file.delete` | Xóa mềm file vào Thùng rác. |
| **Files** | `GET` | `/api/files/trash` | `file.read` | Lấy danh sách file trong thùng rác có phân trang. |
| **Files** | `POST` | `/api/files/{id}/restore` | `file.upload` | Khôi phục file từ thùng rác. |
| **Files** | `DELETE`| `/api/files/{id}/permanent`| `file.delete` | Xóa vĩnh viễn file trên Cloud và CSDL, hoàn trả Quota. |
| **Files** | `PUT` | `/api/files/{id}/star` | `file.read` | Đổi trạng thái gắn dấu sao. |
| **Files** | `POST` | `/api/files/{id}/share` | `file.read` | Chia sẻ file nội bộ cho đồng nghiệp qua email. |
| **Folders**| `POST` | `/api/folders` | `file.upload` | Tạo thư mục mới. |
| **Profile**| `GET` | `/api/admin/users/{id}` | `user.read.self` | Lấy chi tiết hồ sơ người dùng hiện tại. |
| **Profile**| `PUT` | `/api/admin/users/{id}` | `user.manage` | Cập nhật thông tin hồ sơ cá nhân. |
| **Security**| `POST` | `/api/auth/change-password` | Authorized | Đổi mật khẩu tài khoản. |
| **Quota** | `POST` | `/api/users/request-quota` | Authorized | Gửi phiếu yêu cầu nâng hạn mức dung lượng. |

---

## 6. Error Handling & Contracts (ENG-02 Safe Error Contract)

Tất cả các API trả về theo cấu trúc chuẩn `Result<T>`:
```json
{
  "isSuccess": false,
  "value": null,
  "error": "Thông điệp lỗi thân thiện với người dùng"
}
```

- **HTTP 400 Bad Request:** Lỗi dữ liệu đầu vào không hợp lệ, vượt quá số lượng 20 file, sai mật khẩu cũ.
- **HTTP 401 Unauthorized:** Phiên đăng nhập hết hạn hoặc chưa đăng nhập → Tự động điều hướng về `/login`.
- **HTTP 403 Forbidden:** Không có quyền truy cập hoặc thao tác trên tài nguyên của người khác / phòng ban khác.
- **HTTP 404 Not Found:** Tệp tin hoặc thư mục không tồn tại.
- **HTTP 413 Payload Too Large:** Dung lượng tệp tin vượt quá 100 MB.
- **HTTP 500 Internal Server Error:** Lỗi hệ thống máy chủ hoặc lỗi kết nối Cloud Provider → Log an toàn phía server, trả về thông điệp thân thiện không làm rò rỉ stack trace.

---

## 7. Acceptance Criteria (DoD Kiểm thử & Nghiệm thu)

### 7.1 Nghiệm thu Nhóm Tính năng Tệp tin
- [ ] Tải lên thành công 1 tệp tin hợp lệ: File xuất hiện trong danh sách và lưu trên Cloud Storage.
- [ ] Kéo-thả tải lên hàng loạt 5 file cùng lúc: Hệ thống hiển thị thanh tiến trình và thông báo kết quả từng file.
- [ ] Tải lên file đuôi cấm (`.exe`, `.sh`): Hệ thống từ chối tải lên và hiển thị cảnh báo bảo mật.
- [ ] Tải lên file dung lượng vượt Quota cá nhân: Hệ thống báo lỗi hết hạn mức và chặn lưu trữ.
- [ ] Tải lên file virus mẫu: ClamAV chặn tải, ghi Audit Log và hiển thị cảnh báo phát hiện mã độc.
- [ ] Xem trước file Ảnh, PDF, Code: Modal hiển thị chuẩn xác nội dung.
- [ ] Tải xuống file: File tải về nguyên vẹn nội dung và tên file gốc.
- [ ] Gắn dấu sao: File xuất hiện trong tab "Có gắn dấu sao"; bỏ dấu sao file biến mất khỏi tab.
- [ ] Chia sẻ file: Người được mời xem được file trong tab "Được chia sẻ với tôi".
- [ ] Xóa mềm: File biến mất khỏi danh sách chính và chuyển sang tab "Thùng rác", Quota chưa bị trừ.
- [ ] Khôi phục: File rời khỏi Thùng rác và trở lại danh sách chính.
- [ ] Xóa vĩnh viễn: File bị xóa trên Cloud, bản ghi CSDL bị xóa và Quota được hoàn trả ngay lập tức.

### 7.2 Nghiệm thu Nhóm Tính năng Dung lượng & Hồ sơ
- [ ] Xem Quota: Các chỉ số dung lượng khớp 100% với dữ liệu CSDL.
- [ ] Gửi yêu cầu tăng Quota: Form gửi thành công và ghi nhận thông báo tới Trưởng phòng.
- [ ] Chỉnh sửa hồ sơ: Họ tên, số điện thoại, avatar cập nhật thành công và hiển thị đồng bộ.
- [ ] Đổi mật khẩu: Mật khẩu cũ đúng đổi thành công; mật khẩu cũ sai báo lỗi không khớp.
- [ ] Nhật ký hoạt động: Ghi nhận đầy đủ các thao tác upload, xóa, khôi phục của người dùng.

---

## 8. Out of Scope (Phạm vi Loại trừ)

- Quản lý hạn mức dung lượng toàn doanh nghiệp (thuộc `TenantAdmin`).
- Phân bổ Quota cho các thành viên khác (thuộc `DepartmentAdmin`).
- Khởi tạo và thiết lập phòng ban (thuộc `DepartmentAdmin`).
- Cấu hình hạ tầng Cloud Storage Provider AWS/Azure/Google (thuộc `SystemAdmin`).

---

## 9. Adversarial Quality Pass (Đánh giá Rủi ro Đối nghịch)

### 9.1 Pre-mortem (Giả định sự cố)
- **Kịch bản 1: Đứt kết nối mạng giữa chừng khi upload file 90MB.**
  - *Hậu quả:* Cloud Storage có thể chứa file rác mà CSDL chưa tạo bản ghi.
  - *Giải pháp:* Khối `catch` trong backend kích hoạt lệnh bồi hoàn (Compensating action) xóa file vật lý đã upload nếu thao tác ghi DB thất bại.
- **Kịch bản 2: Người dùng mở 2 tab cùng bấm xóa vĩnh viễn 1 file.**
  - *Hậu quả:* Race condition làm trừ dung lượng Quota hai lần.
  - *Giải pháp:* Transaction CSDL ở mức cô lập `IsolationLevel.Serializable` kèm kiểm tra `lockedFile == null` sau khi đặt khóa dòng. Tab thứ hai sẽ báo lỗi file đã bị xóa và không trừ Quota trùng lặp.
- **Kịch bản 3: Tấn công thay đổi tham số IDOR khi tải xuống file mật.**
  - *Hậu quả:* Lộ lọt tài liệu nhạy cảm của người khác.
  - *Giải pháp:* Backend kiểm tra nghiêm ngặt `fileEntry.TenantId == currentTenantId` và `(fileEntry.OwnerId == currentUserId || fileEntry.DepartmentId == currentDeptId || isShared)`. Bị từ chối ngay nếu không thỏa mãn.

---

## 10. Feature Lock

- **Locked Scope:** Toàn bộ 14 Use Case của Người dùng cuối (`User Portal`) bao gồm Quản lý vòng đời tệp tin, Theo dõi hạn mức Quota, và Quản lý hồ sơ cá nhân / bảo mật.
- **Deferred Work:** Tự động dọn thùng rác theo lịch cron định kỳ (Retention Policy); Chỉnh sửa cộng tác văn bản trực tiếp theo thời gian thực (Real-time Collaborative Editing).
- **Change Path:** `/sdd-update --feature=user --artifact=spec --bump=minor --reason="..."`
- **Lock Boundary:** Phân hệ `User Portal` (`user`), không làm thay đổi các phân hệ quản trị cấp trên (`department-admin`, `tenant-admin`, `system-admin`).

---

## 11. AI Agent Recommendation & Human Final Review

### AI Agent Recommendation
- **Đề xuất:** Phê duyệt Master `SPEC.md` cho phân hệ `user` với cấp độ `DETAILED`. Đặc tả này đã phủ kín toàn bộ 14 chức năng người dùng cuối có thể thực hiện, đảm bảo tuân thủ đầy đủ chuẩn Clean Architecture CQRS và các tiêu chuẩn an ninh thông tin OWASP.
- **Rủi ro:** Cần đảm bảo hệ thống backend đang chạy đầy đủ các service phụ trợ (ClamAV antivirus và Cloud Storage credentials) để các ca kiểm thử tích hợp đạt kết quả tốt nhất.

### Human Final Review
- **Trạng thái:** `PENDING HUMAN REVIEW`
- **Người duyệt:** PENDING
- **Quyết định:** PENDING
- **Thời gian:** PENDING
- **Ghi chú/Chỉ đạo:** Chờ Human Reviewer xem xét và phê duyệt với lệnh `/sdd-review` trước khi tiến hành viết PLAN.md và TASKS.md.
