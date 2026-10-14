# Feature Specification: Restore File Subsystem (SPEC.md)

- **Feature Slug:** `file-restore`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Risk Posture:** `MEDIUM`
- **Review Route:** `Architecture & Security Review Mandatory`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Context & Goal

### 1.1 Bối cảnh
Người dùng có nhu cầu khôi phục tệp tin đã xóa mềm trong Thùng rác (`POST /api/files/{id}/restore`). Quá trình khôi phục chuyển tệp tin về trạng thái hoạt động chính, giữ nguyên cấu trúc thư mục và liên kết lưu trữ đám mây.

### 1.2 Mục tiêu
- Khôi phục trạng thái tệp tin (`IsDeleted = false`, xóa bỏ `DeletedAt` và `DeletedByUserId`).
- Kiểm tra tính hợp lệ và phân quyền theo Role, ngăn chặn hành vi can thiệp trái phép.
- Ghi vết kiểm toán `AuditLog` rõ ràng.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn khôi phục |
| :--- | :--- | :--- |
| **Employee (User)** | `file.upload` | Chỉ khôi phục các file do mình sở hữu HOẶC thuộc phòng ban của mình. |
| **DepartmentAdmin** | `file.upload` | Khôi phục các file thuộc phòng ban mình quản lý. |
| **TenantAdmin** | `file.upload`, `file.*` | Khôi phục bất kỳ file nào thuộc tổ chức của mình. |
| **SystemAdmin** | Toàn quyền | Khôi phục file trên toàn hệ thống. |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực Request
- **REQ-RF-01 (Event-driven):** `WHEN` client gửi yêu cầu `POST /api/files/{id}/restore`, hệ thống API `SHALL` kiểm tra JWT token và xác thực quyền `file.upload`.
- **REQ-RF-02 (Unwanted):** `IF` request thiếu token xác thực hoặc không có quyền `file.upload`, `THEN` hệ thống `SHALL` từ chối với HTTP `401 Unauthorized` hoặc `403 Forbidden`.

### 3.2 Kiểm tra Trạng Thái & Phân Quyền (Anti-IDOR)
- **REQ-RF-03 (Event-driven):** `WHEN` `RestoreFileCommandHandler` nhận lệnh `RestoreFileCommand(FileId)`, hệ thống `SHALL` truy vấn `FileEntry` có `f.Id == request.FileId && f.IsDeleted == true`.
- **REQ-RF-04 (Unwanted):** `IF` tệp tin không tồn tại trong CSDL hoặc không nằm trong thùng rác (`f.IsDeleted == false`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại trong thùng rác hoặc đã được khôi phục."`
- **REQ-RF-05 (Unwanted):** `IF` người gọi không phải `SystemAdmin` VÀ (`TenantId` của người gọi không khớp `fileEntry.TenantId`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại trong thùng rác hoặc không thuộc quyền quản lý."` (Fail-Closed).
- **REQ-RF-06 (Unwanted):** `IF` người gọi là `Employee` VÀ (không phải chủ sở hữu `fileEntry.OwnerId != callerUserId` VÀ không cùng phòng ban `fileEntry.DepartmentId != callerDepartmentId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn không có quyền khôi phục tệp tin này."`
- **REQ-RF-07 (Unwanted):** `IF` người gọi là `DepartmentAdmin` VÀ `fileEntry.DepartmentId` khác với phòng ban của caller, `THEN` hệ thống `SHALL` từ chối với lỗi: `"Bạn không có quyền khôi phục tệp tin của phòng ban khác."`

### 3.3 Khôi Phục Trạng Thái & Ghi Nhận Kiểm Toán
- **REQ-RF-08 (Event-driven):** `WHEN` xác thực quyền sở hữu hợp lệ, hệ thống `SHALL` cập nhật bản ghi:
  - `fileEntry.IsDeleted = false`
  - `fileEntry.DeletedAt = null`
  - `fileEntry.DeletedByUserId = null`
- **REQ-RF-09 (Event-driven):** `WHEN` cập nhật trạng thái xong, hệ thống `SHALL` thêm bản ghi `AuditLog`:
  - `Action = "RestoreFromTrash"`
  - `EntityName = "FileEntry"`
  - `EntityId = fileEntry.Id`
  - `Details = $"Đã khôi phục tệp tin '{fileEntry.Name}' ({fileEntry.SizeBytes} bytes) từ thùng rác."`
- **REQ-RF-10 (Event-driven):** `WHEN` `SaveChangesAsync()` thành công, hệ thống `SHALL` trả về `Result<bool>.Success(true)`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Access Control):** Bảo đảm quy tắc phân quyền chặt chẽ; nhân viên chỉ khôi phục file của bản thân hoặc phòng ban mình.
- **NFR-PERF-01 (Speed):** Thao tác khôi phục chỉ cập nhật cờ logic trên CSDL, không tốn tài nguyên I/O mạng trên Cloud Storage Provider.

---

## 5. Data Contracts & Errors

```csharp
public record RestoreFileCommand(Guid FileId) : IRequest<Result<bool>>;
```

| Tình huống lỗi | Thông báo phản hồi |
| :--- | :--- |
| File không có trong thùng rác / Đã khôi phục | `"Tệp tin không tồn tại trong thùng rác hoặc đã được khôi phục."` |
| Sai Tenant / Không thuộc quyền quản lý | `"Tệp tin không tồn tại trong thùng rác hoặc không thuộc quyền quản lý."` |
| Employee không có quyền | `"Bạn không có quyền khôi phục tệp tin này."` |
| DepartmentAdmin sai phòng ban | `"Bạn không có quyền khôi phục tệp tin của phòng ban khác."` |

---

## 6. Definition of Done (DoD)

- [x] Đặc tả đầy đủ các quy tắc EARS cho luồng khôi phục tệp tin.
- [x] Phân quyền kiểm tra chặt chẽ giữa Employee, DepartmentAdmin và TenantAdmin.
- [x] Ghi nhận `AuditLog` chuẩn hóa.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** SPEC khôi phục tệp tin đáp ứng đúng quy trình nghiệp vụ và tiêu chuẩn bảo mật.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
