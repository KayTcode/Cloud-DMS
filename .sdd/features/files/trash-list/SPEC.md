# Feature Specification: Recycle Bin Management (SPEC.md)

- **Feature Slug:** `file-trash-list`
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
Người dùng cần một giao diện Thùng rác (Recycle Bin) để theo dõi các tệp tin đã xóa mềm, biết được ai đã xóa và dung lượng đang chiếm dụng. Endpoint `GET /api/files/trash` tiếp nhận yêu cầu và trả về danh sách tệp tin trong thùng rác có phân quyền chặt chẽ theo Role.

### 1.2 Mục tiêu
- Phân quyền hiển thị nghiêm ngặt: Nhân viên chỉ thấy file của mình, Trưởng phòng thấy file của phòng ban, Quản trị viên tổ chức thấy toàn bộ.
- Hỗ trợ phân trang an toàn (1 đến 100 bản ghi/trang), tìm kiếm theo tên tệp tin và tính toán tổng dung lượng rác (`totalSizeBytes`).
- Truy vấn hiệu năng cao với `AsNoTracking()`.

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Phạm vi hiển thị trong Thùng rác |
| :--- | :--- | :--- |
| **Employee (User)** | `file.read` | Chỉ thấy các file có `OwnerId == callerId` HOẶC `DeletedByUserId == callerId`. |
| **DepartmentAdmin** | `file.read` | Xem toàn bộ các file trong thùng rác thuộc phòng ban của mình (`DepartmentId`). |
| **TenantAdmin** | `file.read`, `file.*` | Xem toàn bộ file trong thùng rác của toàn tổ chức, có thể lọc theo `DepartmentId`. |
| **SystemAdmin** | Toàn quyền | Xem thùng rác theo bất kỳ `TenantId` nào được truyền trong query. |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực Request
- **REQ-TL-01 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files/trash` kèm các query parameter (`tenantId`, `departmentId`, `pageNumber`, `pageSize`, `searchTerm`), hệ thống `SHALL` kiểm tra chính sách `[Authorize(Policy = "file.read")]`.
- **REQ-TL-02 (Unwanted):** `IF` request thiếu token xác thực hợp lệ, `THEN` hệ thống `SHALL` trả về HTTP `401 Unauthorized`.

### 3.2 Xác định Tổ chức & Phân Quyền Vai Trò
- **REQ-TL-03 (State-driven):** `WHILE` caller là `SystemAdmin`, hệ thống `SHALL` yêu cầu tham số `request.TenantId`. Nếu thiếu, trả về lỗi: `"Vui lòng chỉ định Tổ chức (Tenant) để xem thùng rác."`
- **REQ-TL-04 (State-driven):** `WHILE` caller không phải `SystemAdmin`, hệ thống `SHALL` tự động lấy `tenantId` từ token của caller. Nếu không xác định được Tenant, trả về lỗi: `"Không xác định được tổ chức/tenant."`
- **REQ-TL-05 (State-driven):** `WHILE` caller có vai trò là `Employee`, hệ thống `SHALL` bổ sung bộ lọc: `f.OwnerId == callerId || f.DeletedByUserId == callerId`.
- **REQ-TL-06 (State-driven):** `WHILE` caller có vai trò là `DepartmentAdmin`, hệ thống `SHALL` bổ sung bộ lọc: `f.DepartmentId == callerDepartmentId`.
- **REQ-TL-07 (Event-driven):** `WHEN` request có truyền `departmentId` hợp lệ, hệ thống `SHALL` lọc chính xác theo phòng ban đó.

### 3.3 Tìm Kiếm, Đếm & Phân Trang
- **REQ-TL-08 (Ubiquitous):** Hệ thống `SHALL` áp dụng bộ lọc cơ sở: `f.TenantId == tenantId && f.IsDeleted == true`.
- **REQ-TL-09 (Event-driven):** `WHEN` request có chứa `searchTerm`, hệ thống `SHALL` tìm kiếm theo mẫu `LIKE %searchTerm%` trên cột `f.Name`.
- **REQ-TL-10 (Ubiquitous):** Hệ thống `SHALL` thực hiện đếm tổng số lượng bản ghi thỏa mãn (`totalCount`) và tính tổng dung lượng chiếm dụng (`totalSizeBytes`) bằng `SumAsync(f => f.SizeBytes)`.
- **REQ-TL-11 (Ubiquitous):** Hệ thống `SHALL` chuẩn hóa kích thước trang: `pageNumber = pageNumber <= 0 ? 1 : pageNumber`, `pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100)`.
- **REQ-TL-12 (Ubiquitous):** Hệ thống `SHALL` sắp xếp giảm dần theo thời gian xóa: `f.DeletedAt ?? f.CreatedAt DESC`.
- **REQ-TL-13 (Event-driven):** `WHEN` lấy dữ liệu thành công, hệ thống `SHALL` ánh xạ sang `TrashFileItemDto` (gồm thông tin người xóa `DeletedByUser`, phòng ban, tên provider) và trả về HTTP `200 OK` với DTO `TrashFileListDto`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Privacy Protection):** Nhân viên không thể xem trộm tệp tin của đồng nghiệp trong thùng rác trừ khi chính nhân viên đó đã thực hiện thao tác xóa.
- **NFR-PERF-01 (Read Optimization):** Toàn bộ truy vấn sử dụng `AsNoTracking()` để loại bỏ overhead theo dõi thực thể của EF Core.

---

## 5. Data Contracts

```csharp
public record GetTrashFilesQuery : IRequest<Result<TrashFileListDto>>
{
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
}

public record TrashFileListDto(
    List<TrashFileItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    long TotalSizeBytes
);
```

---

## 6. Definition of Done (DoD)

- [x] Phân quyền hiển thị theo Role chặt chẽ.
- [x] Tính toán chính xác tổng số lượng và tổng dung lượng rác `TotalSizeBytes`.
- [x] Phân trang chuẩn hóa với giới hạn an toàn tối đa 100 bản ghi/trang.

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** SPEC đáp ứng đầy đủ yêu cầu quản lý thùng rác, bảo vệ quyền riêng tư cá nhân và tối ưu hóa truy vấn.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
