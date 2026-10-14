# Feature Specification: Soft Delete File Subsystem (SPEC.md)

- **Feature Slug:** `file-soft-delete`
- **Module:** `Core Document Management / Storage Subsystem`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Spec Depth Baseline:** `DETAILED`
- **Risk Posture:** `HIGH`
- **Review Route:** `Architecture & Security Review Mandatory`
- **Decision Owner:** Tech Lead & Security Lead (@arch-board)

---

## 1. Context & Goal

### 1.1 Bối cảnh
Khi người dùng xóa một tệp tin từ giao diện chính của hệ thống DMS (`DELETE /api/files/{id}`), hệ thống không được phép xóa vật lý tệp tin ngay lập tức. Tệp tin cần được chuyển vào Thùng rác (`IsDeleted = true`) để cho phép xem xét, khôi phục hoặc xóa vĩnh viễn sau đó.

### 1.2 Mục tiêu
- Bảo đảm tính toàn vẹn của dữ liệu và phòng chống xóa nhầm.
- Ngăn chặn triệt để hành vi leo quyền IDOR xóa tệp tin của người khác hoặc tổ chức khác.
- Duy trì tính nhất quán của giao dịch CSDL với mức cô lập `Serializable`.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn xóa tệp tin |
| :--- | :--- | :--- |
| **Employee (User)** | `file.delete` | Chỉ được xóa mềm các file thuộc phòng ban của mình (`DepartmentId`). |
| **DepartmentAdmin** | `file.delete` | Xóa mềm các file trong phạm vi phòng ban mình quản lý. |
| **TenantAdmin** | `file.delete`, `file.*` | Xóa mềm bất kỳ file nào thuộc tổ chức của mình. |
| **SystemAdmin** | Toàn quyền | Xóa mềm file trên toàn hệ thống. |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Phân quyền API
- **REQ-SD-01 (Event-driven):** `WHEN` client gửi HTTP request `DELETE /api/files/{id}`, hệ thống API `SHALL` kiểm tra JWT token và xác thực quyền `file.delete`.
- **REQ-SD-02 (Unwanted):** `IF` request thiếu token hợp lệ hoặc không có quyền `file.delete`, `THEN` hệ thống `SHALL` trả về HTTP `401 Unauthorized` hoặc `403 Forbidden`.

### 3.2 Kiểm tra Sự Tồn Tại & Quyền Sở Hữu (Anti-IDOR)
- **REQ-SD-03 (Event-driven):** `WHEN` `DeleteFileCommandHandler` nhận lệnh `DeleteFileCommand(FileId)`, hệ thống `SHALL` truy vấn `FileEntry` có `f.Id == request.FileId && !f.IsDeleted`.
- **REQ-SD-04 (Unwanted):** `IF` tệp tin không tồn tại hoặc đã bị đánh dấu xóa mềm từ trước, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."`
- **REQ-SD-05 (Unwanted):** `IF` người gọi không phải `SystemAdmin` VÀ (`TenantId` của người gọi không khớp `fileEntry.TenantId`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."` (Fail-Closed).
- **REQ-SD-06 (Unwanted):** `IF` người gọi là `Employee` VÀ `fileEntry.DepartmentId` khác với `DepartmentId` của người gọi, `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Tệp tin không tồn tại hoặc đã bị xóa."`

### 3.3 Đánh Dấu Xóa Mềm Trong Serializable Transaction
- **REQ-SD-07 (Event-driven):** `WHEN` xác thực quyền sở hữu hợp lệ, hệ thống `SHALL` mở một CSDL Transaction với mức độ cô lập `IsolationLevel.Serializable` qua `CreateExecutionStrategy`.
- **REQ-SD-08 (State-driven):** `WHILE` thực hiện giao dịch, hệ thống `SHALL` truy vấn lại bản ghi tệp tin có khóa dòng (`lockedFile`), kiểm tra lại điều kiện `!lockedFile.IsDeleted`.
- **REQ-SD-09 (Event-driven):** `WHEN` kiểm tra hợp lệ, hệ thống `SHALL` cập nhật:
  - `lockedFile.IsDeleted = true`
  - `lockedFile.DeletedAt = DateTime.UtcNow`
  - `lockedFile.DeletedByUserId = callerUserId`
- **REQ-SD-10 (State-driven):** `WHILE` tệp tin đang ở trạng thái xóa mềm, hệ thống `SHALL NOT` gọi lệnh xóa file trên Cloud Storage và `SHALL NOT` hoàn trả dung lượng Quota của User, Department hay Tenant.
- **REQ-SD-11 (Event-driven):** `WHEN` cập nhật thành công, hệ thống `SHALL` thêm bản ghi `AuditLog`:
  - `Action = "MoveToTrash"`
  - `EntityName = "FileEntry"`
  - `EntityId = lockedFile.Id`
  - `Details = $"Đã chuyển tệp tin '{lockedFile.Name}' ({lockedFile.SizeBytes} bytes) vào thùng rác."`
- **REQ-SD-12 (Event-driven):** `WHEN` `SaveChangesAsync()` thành công, transaction `SHALL` được commit và API trả về `Result<bool>.Success(true)`.
- **REQ-SD-13 (Unwanted):** `IF` có lỗi ngoại lệ xảy ra trong transaction, `THEN` hệ thống `SHALL` gọi `transaction.RollbackAsync()`, xóa ChangeTracker và ném ngoại lệ để xử lý tập trung.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (IDOR Defense):** Toàn bộ các phản hồi từ chối do sai Tenant hoặc sai Department đều trả về cùng một thông điệp Fail-Closed: `"Tệp tin không tồn tại hoặc đã bị xóa."`
- **NFR-DATA-01 (Data Integrity):** Giao dịch Serializable đảm bảo không có hai tiến trình đồng thời cùng xóa một tệp tin hoặc gây xung đột trạng thái dữ liệu.

---

## 5. Data Contracts & Errors

```csharp
public record DeleteFileCommand(Guid FileId) : IRequest<Result<bool>>;
```

| Error Situation | Error Message |
| :--- | :--- |
| File không tồn tại / Đã xóa / Sai Tenant / Sai Department | `"Tệp tin không tồn tại hoặc đã bị xóa."` |
| Thành công | `Result<bool>.Success(true)` |

---

## 6. Definition of Done (DoD)

- [x] Quy định cụ thể quy tắc bảo toàn file trên Cloud và Quota khi xóa mềm.
- [x] Ràng buộc transaction `Serializable` và ghi nhận đầy đủ `AuditLog`.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** SPEC đảm bảo đúng nguyên tắc an toàn dữ liệu 2 giai đoạn và cơ chế phân quyền bảo mật cao.
- **Risk:** High.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
