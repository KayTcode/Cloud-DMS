# Feature Specification: Permanent Delete File Subsystem (SPEC.md)

- **Feature Slug:** `file-permanent-delete`
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
Khi một tệp tin đang nằm trong Thùng rác (`IsDeleted == true`), người dùng hoặc quản trị viên có thẩm quyền có thể chọn xóa vĩnh viễn (`DELETE /api/files/{id}/permanent`). Thao tác này tiêu hủy tệp tin vật lý trên Cloud Storage và xóa bản ghi CSDL, đồng thời hoàn trả dung lượng sử dụng cho toàn bộ các cấp hạn ngạch.

### 1.2 Mục tiêu
- Xóa vật lý tệp tin trên Cloud Provider mà không làm nghẽn kết nối CSDL.
- Hoàn trả chính xác dung lượng Quota cho 4 tầng: StorageProvider, Tenant, Department, User trong giao dịch cô lập `Serializable`.
- Phân quyền chặt chẽ: Nhân viên chỉ xóa được file do mình sở hữu, ngăn chặn rủi ro phá hoại dữ liệu.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Quyền hạn xóa vĩnh viễn |
| :--- | :--- | :--- |
| **Employee (User)** | `file.delete` | Chỉ được xóa vĩnh viễn tệp tin do mình sở hữu (`OwnerId == callerUserId`). |
| **DepartmentAdmin** | `file.delete` | Chỉ xóa vĩnh viễn tệp tin thuộc phòng ban của mình (`DepartmentId`). |
| **TenantAdmin** | `file.delete`, `file.*` | Xóa vĩnh viễn bất kỳ tệp tin nào trong tổ chức của mình. |
| **SystemAdmin** | Toàn quyền | Xóa vĩnh viễn tệp tin trên toàn hệ thống. |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực
- **REQ-PD-01 (Event-driven):** `WHEN` client gửi HTTP request `DELETE /api/files/{id}/permanent`, hệ thống API `SHALL` kiểm tra JWT token và xác thực quyền `file.delete`.
- **REQ-PD-02 (Unwanted):** `IF` request thiếu token hợp lệ hoặc không có quyền `file.delete`, `THEN` hệ thống `SHALL` trả về HTTP `401 Unauthorized` hoặc `403 Forbidden`.

### 3.2 Kiểm tra Trạng Thái & Phân Quyền (Anti-IDOR)
- **REQ-PD-03 (Event-driven):** `WHEN` `PermanentDeleteFileCommandHandler` nhận lệnh `PermanentDeleteFileCommand(FileId)`, hệ thống `SHALL` truy vấn `FileEntry` kèm `StorageProvider` với điều kiện `f.Id == request.FileId && f.IsDeleted == true`.
- **REQ-PD-04 (Unwanted):** `IF` tệp tin không tồn tại trong CSDL hoặc chưa bị xóa mềm (`f.IsDeleted == false`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại trong thùng rác hoặc đã bị xóa vĩnh viễn."`
- **REQ-PD-05 (Unwanted):** `IF` người gọi không phải `SystemAdmin` VÀ (`TenantId` của người gọi không khớp `fileEntry.TenantId`), `THEN` hệ thống `SHALL` trả về lỗi: `"Tệp tin không tồn tại trong thùng rác hoặc không thuộc quyền quản lý."`
- **REQ-PD-06 (Unwanted):** `IF` người gọi là `Employee` VÀ không phải chủ sở hữu tệp tin (`fileEntry.OwnerId != callerUserId`), `THEN` hệ thống `SHALL` từ chối thao tác với lỗi: `"Bạn không có quyền xóa vĩnh viễn tệp tin này."`
- **REQ-PD-07 (Unwanted):** `IF` người gọi là `DepartmentAdmin` VÀ `fileEntry.DepartmentId` khác với phòng ban của caller, `THEN` hệ thống `SHALL` từ chối với lỗi: `"Bạn không có quyền xóa vĩnh viễn tệp tin của phòng ban khác."`

### 3.3 Xóa File Vật Lý Trên Cloud Storage (Bước 1)
- **REQ-PD-08 (Event-driven):** `WHEN` xác thực quyền thành công, hệ thống `SHALL` gọi dịch vụ lưu trữ tương ứng `storageService.DeleteFileAsync(fileEntry.StorageKey)` bên ngoài Transaction CSDL.
- **REQ-PD-09 (Ubiquitous):** Nếu lệnh xóa Cloud ném ngoại lệ hoặc file vật lý không còn tồn tại trên Cloud, hệ thống `SHALL` bỏ qua ngoại lệ và tiếp tục Bước 2 nhằm đảm bảo giải phóng dung lượng CSDL và hạn mức Quota cho doanh nghiệp.

### 3.4 Hoàn Trả Quota & Xóa Bản Ghi Trong Serializable Transaction (Bước 2)
- **REQ-PD-10 (Event-driven):** `WHEN` hoàn tất Bước 1, hệ thống `SHALL` mở một giao dịch CSDL với mức độ cô lập `IsolationLevel.Serializable` qua `CreateExecutionStrategy`.
- **REQ-PD-11 (State-driven):** `WHILE` thực hiện giao dịch, hệ thống `SHALL` truy vấn lại bản ghi tệp tin có khóa dòng (`lockedFile`). Nếu `lockedFile == null`, rollback và trả về: `"Tệp tin đã bị xóa."`
- **REQ-PD-12 (Event-driven):** `WHEN` có `lockedFile`, hệ thống `SHALL` hoàn trả dung lượng phân tầng:
  1. `StorageProvider.UsedCapacityBytes = Math.Max(0, UsedCapacityBytes - lockedFile.SizeBytes)`.
  2. `Tenant.StorageUsedBytes = Math.Max(0, StorageUsedBytes - lockedFile.SizeBytes)`.
  3. `Department.StorageUsedBytes = Math.Max(0, StorageUsedBytes - lockedFile.SizeBytes)` (theo `lockedFile.DepartmentId` hoặc phòng ban của chủ sở hữu).
  4. `UserStorageQuota.UsedBytes = Math.Max(0, UsedBytes - lockedFile.SizeBytes)` (cho chủ sở hữu `lockedFile.OwnerId` nếu có cấu hình).
- **REQ-PD-13 (Event-driven):** `WHEN` cập nhật xong dung lượng, hệ thống `SHALL`:
  1. Thêm bản ghi `AuditLog` với `Action = "PermanentDelete"`, `EntityName = "FileEntry"`, `EntityId = lockedFile.Id`, kèm chi tiết số bytes đã giải phóng.
  2. Gọi `_context.FileEntries.Remove(lockedFile)` để xóa vĩnh viễn bản ghi khỏi CSDL.
- **REQ-PD-14 (Event-driven):** `WHEN` `SaveChangesAsync()` thành công, transaction `SHALL` được commit và API trả về `Result<bool>.Success(true)`.
- **REQ-PD-15 (Unwanted):** `IF` xảy ra ngoại lệ trong transaction, hệ thống `SHALL` rollback toàn bộ giao dịch, xóa ChangeTracker và ném ngoại lệ để xử lý tập trung.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-DATA-01 (No Deadlock):** Việc gọi Cloud Storage API ngoài transaction CSDL ngăn chặn hiện tượng giữ khóa bảng quá lâu dẫn đến Deadlock khi mạng chập chờn.
- **NFR-DATA-02 (Quota Consistency):** Sử dụng `IsolationLevel.Serializable` đảm bảo việc tính toán và trừ dung lượng Quota diễn ra nguyên khối, không xảy ra sai lệch số liệu.
- **NFR-SEC-01 (Strict Ownership):** Nhân viên chỉ có thể hủy vĩnh viễn tài liệu do chính mình tạo ra.

---

## 5. Data Contracts & Errors

```csharp
public record PermanentDeleteFileCommand(Guid FileId) : IRequest<Result<bool>>;
```

| Tình huống lỗi | Thông báo phản hồi |
| :--- | :--- |
| File không trong thùng rác / Chưa xóa mềm | `"Tệp tin không tồn tại trong thùng rác hoặc đã bị xóa vĩnh viễn."` |
| Sai Tenant / Không thuộc quyền quản lý | `"Tệp tin không tồn tại trong thùng rác hoặc không thuộc quyền quản lý."` |
| Employee không phải chủ sở hữu | `"Bạn không có quyền xóa vĩnh viễn tệp tin này."` |
| DepartmentAdmin sai phòng ban | `"Bạn không có quyền xóa vĩnh viễn tệp tin của phòng ban khác."` |

---

## 6. Definition of Done (DoD)

- [x] Quy trình 2 bước xóa Cloud độc lập với Transaction CSDL.
- [x] Hoàn trả chính xác dung lượng Quota 4 tầng không bị âm dung lượng.
- [x] Phân quyền chặt chẽ chống phá hoại dữ liệu.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** SPEC xóa vĩnh viễn tệp tin đáp ứng tiêu chuẩn an toàn dữ liệu, giải phóng hạn ngạch và phòng ngừa lỗi deadlock hệ thống.
- **Risk:** High.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
