# Feature Specification: File Listing & Storage Quota Summary (SPEC.md)

- **Feature Slug:** `file-get-list`
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
Người dùng cần truy cập danh sách tài liệu đang hoạt động để xem thông tin, tìm kiếm, điều hướng cây thư mục và theo dõi hạn mức sử dụng đĩa trực tiếp trên giao diện người dùng. API `GET /api/files` cung cấp đồng thời danh sách tệp tin và báo cáo hạn mức sử dụng Quota.

### 1.2 Mục tiêu
- Cung cấp danh sách tệp tin được phân trang, sắp xếp theo thời gian tạo mới nhất (`CreatedAt DESC`).
- Bảo đảm cô lập dữ liệu đa người thuê (Multi-Tenancy) và phân quyền chặt chẽ theo cấp phòng ban (Department Scope).
- Tính toán chính xác các chỉ số Quota (`quota`, `used`, `available`, `usagePercent`).

---

## 2. Actors & Permissions

| Actor / Role | Policy / Permission | Phạm vi truy cập dữ liệu |
| :--- | :--- | :--- |
| **Employee (User)** | `file.read` | Tự động bị giới hạn chỉ xem tài liệu thuộc phòng ban của mình (`DepartmentId`). |
| **DepartmentAdmin** | `file.read` | Xem toàn bộ tài liệu thuộc phòng ban mình quản trị. |
| **TenantAdmin** | `file.read`, `file.*` | Xem toàn bộ tài liệu của tổ chức, hoặc lọc theo phòng ban chỉ định. |
| **SystemAdmin** | Toàn quyền | Xem tài liệu theo Tenant chỉ định trên toàn hệ thống. |
| **Unauthenticated** | Không có quyền | Bị từ chối với HTTP `401 Unauthorized`. |

---

## 3. Functional Requirements (EARS Specifications)

### 3.1 Tiếp nhận & Xác thực
- **REQ-FL-01 (Event-driven):** `WHEN` client gửi yêu cầu `GET /api/files` kèm các query parameter (`tenantId`, `departmentId`, `folderId`, `pageNumber`, `pageSize`, `searchTerm`), hệ thống `SHALL` kiểm tra chính sách `[Authorize(Policy = "file.read")]`.
- **REQ-FL-02 (Unwanted):** `IF` request thiếu token xác thực, `THEN` hệ thống `SHALL` trả về HTTP `401 Unauthorized`.

### 3.2 Cô lập Tổ chức & Phân cấp Phòng ban
- **REQ-FL-03 (Event-driven):** `WHEN` trích xuất danh tính caller, hệ thống `SHALL` xác định `tenantId` từ `ICurrentUserService.TenantId` hoặc `User.TenantId`.
- **REQ-FL-04 (Unwanted):** `IF` caller không có liên kết Tenant, `THEN` hệ thống `SHALL` trả về lỗi: `"Không xác định được tổ chức/tenant."`
- **REQ-FL-05 (Unwanted):** `IF` request truyền `tenantId` khác với Tenant của caller, `THEN` hệ thống `SHALL` từ chối với lỗi: `"Bạn không có quyền xem dữ liệu của tenant khác."`
- **REQ-FL-06 (State-driven):** `WHILE` caller có vai trò là `Employee` hoặc `DepartmentAdmin`, hệ thống `SHALL` tự động đặt phạm vi phòng ban `targetDepartmentId` về phòng ban của caller nếu request không truyền `departmentId`.
- **REQ-FL-07 (Unwanted):** `IF` `targetDepartmentId` được chỉ định nhưng không tồn tại hoặc không thuộc Tenant của caller hoặc `department.IsActive == false`, `THEN` hệ thống `SHALL` trả về lỗi: `"Phòng ban không tồn tại hoặc không thuộc tổ chức này."`

### 3.3 Lọc, Tìm Kiếm & Truy Vấn Tệp Tin
- **REQ-FL-08 (Ubiquitous):** Hệ thống `SHALL` chỉ truy vấn các bản ghi `FileEntry` có `f.TenantId == tenant.Id` và `f.IsDeleted == false`.
- **REQ-FL-09 (Event-driven):** `WHEN` request có truyền `folderId`, hệ thống `SHALL` lọc chính xác các file có `f.FolderId == request.FolderId.Value`.
- **REQ-FL-10 (Event-driven):** `WHEN` request có truyền `searchTerm`, hệ thống `SHALL` lọc không phân biệt hoa thường theo tên tệp tin: `f.Name.ToLower().Contains(term)`.
- **REQ-FL-11 (Ubiquitous):** Hệ thống `SHALL` sắp xếp danh sách kết quả giảm dần theo thời gian tạo (`f.CreatedAt DESC`).

### 3.4 Tính Toán Hạn Mức Quota
- **REQ-FL-12 (Event-driven):** `WHEN` truy vấn danh sách thành công, hệ thống `SHALL` tính toán Quota:
  - Nếu truy vấn theo phòng ban: `quota = department.StorageQuotaBytes`, `used = department.StorageUsedBytes`.
  - Nếu truy vấn toàn Tenant: `quota = tenant.StorageQuotaBytes`, `used = tenant.StorageUsedBytes`.
  - `available = Math.Max(0, quota - used)`.
  - `usagePercent = quota > 0 ? Math.Round((double)used / quota * 100, 2) : 0`.
- **REQ-FL-13 (Event-driven):** `WHEN` hoàn tất, hệ thống `SHALL` trả về HTTP `200 OK` kèm theo DTO `TenantStorageSummaryDto`.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Data Isolation):** Tuyệt đối không cho phép truy vấn xuyên Tenant; nhân viên chỉ thấy tài liệu phòng ban mình.
- **NFR-PERF-01 (Query Efficiency):** Áp dụng `AsNoTracking()` hoặc chỉ `Select` các cột cần thiết trong DTO để giảm tiêu thụ bộ nhớ EF Core.

---

## 5. Data Contracts

```csharp
public record GetTenantFilesQuery : IRequest<Result<TenantStorageSummaryDto>>
{
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? FolderId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
}

public record TenantStorageSummaryDto(
    Guid TenantId,
    string OrganizationName,
    long QuotaBytes,
    long UsedBytes,
    long AvailableBytes,
    double UsagePercentage,
    Guid? StorageProviderId,
    string? StorageProviderName,
    string? StorageProviderType,
    List<FileEntryDto> Files
);
```

---

## 6. Definition of Done (DoD)

- [x] Đặc tả chi tiết các luồng lọc theo phòng ban, thư mục, tìm kiếm và phân quyền.
- [x] Định nghĩa chuẩn xác công thức tính toán Quota 2 tầng (Tenant vs Department).

---

## 7. AI Recommendation & Human Review

### AI Agent Recommendation
- **Status:** `PENDING HUMAN REVIEW`
- **Assessment:** SPEC đảm bảo phân quyền xem tài liệu chặt chẽ và cung cấp chỉ số dung lượng chính xác cho FE.
- **Risk:** Medium.

### Human Final Review
- **Reviewer:** PENDING HUMAN REVIEW
- **Decision:** PENDING HUMAN REVIEW
- **Timestamp:** PENDING HUMAN REVIEW
