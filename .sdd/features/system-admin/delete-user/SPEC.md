# SPEC: Delete User (Xóa Tài khoản Người dùng)

- **Feature Slug:** `delete-user`
- **Module:** `SystemAdmin`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Nghiệp vụ vô hiệu hóa tài khoản người dùng, bảo vệ SystemAdmin duy nhất, thu hồi Refresh Tokens, và điều phối Idempotent Mutation.
  - Risk Posture: `HIGH` (Bảo mật danh tính, an toàn vận hành Super Admin).
  - Review Route: `Architecture & Security Review Mandatory`

---

## 1. Context & Goal

Cho phép `SystemAdmin` (hoặc người dùng có quyền `user.manage`) xóa một tài khoản người dùng khỏi hệ thống thông qua cơ chế Soft-Delete. Thao tác này:
1. Đặt cờ `IsActive = false` để ngăn người dùng tiếp tục đăng nhập hoặc sử dụng API.
2. Thu hồi (`Revoke/Delete`) toàn bộ `RefreshTokens` đang hoạt động của người dùng đó.
3. Tăng `AuthorizationVersion` để làm mất hiệu lực token truy cập hiện tại.
4. Bảo toàn thực thể `User` trong CSDL nhằm giữ vững tính toàn vẹn khóa ngoại (Foreign Key Restrict) của các bảng nhật ký kiểm toán (`AuditLogs`) và tài liệu (`FileEntries`).
5. Đảm bảo quy tắc an toàn vận hành: **Tuyệt đối cấm xóa tài khoản SystemAdmin duy nhất đang hoạt động trong toàn hệ thống**.

---

## 2. Actors & Roles

- **Actor:** `SystemAdmin` hoặc người dùng sở hữu quyền `user.manage`.
- **Unauthorized:** Mọi người dùng không có quyền `user.manage` hoặc không có vai trò `SystemAdmin` đều bị từ chối với mã lỗi `403 Forbidden` (`"Bạn không có quyền thực hiện thao tác này."`).

---

## 3. Functional Requirements (EARS)

- **EARS-01 (Event-driven):** `WHEN` Caller có quyền `user.manage` (hoặc vai trò `SystemAdmin`) gửi lệnh `DELETE /api/admin/users/{id}` kèm header `Idempotency-Key` hợp lệ, `THEN` hệ thống `SHALL` điều phối qua `IAuthorizationMutationCoordinator`, nạp thông tin người dùng cùng vai trò (`UserRoles`), đặt `IsActive = false`, tăng `AuthorizationVersion` thêm 1, thu hồi toàn bộ `RefreshTokens` của người dùng đó, lưu thay đổi vào CSDL và trả về kết quả thành công (`200 OK` hoặc `202 Accepted`).
- **EARS-02 (Unwanted):** `IF` tài khoản bị yêu cầu xóa mang vai trò `SystemAdmin` và tổng số tài khoản `SystemAdmin` đang hoạt động trong toàn hệ thống `<= 1`, `THEN` hệ thống `SHALL` từ chối thao tác và trả về `Result.Failure("Cannot delete the only System Administrator in the system.")` với mã lỗi `400 Bad Request`.
- **EARS-03 (Unwanted):** `IF` không tìm thấy người dùng theo `Id` được cung cấp, `THEN` hệ thống `SHALL` trả về `Result.Failure("User with ID '{id}' was not found.")` với mã lỗi `404 Not Found`.
- **EARS-04 (Unwanted):** `IF` Caller không sở hữu quyền `user.manage` và không có vai trò `SystemAdmin`, `THEN` hệ thống `SHALL` từ chối thao tác ngay tại tầng Authorization Middleware với mã lỗi `403 Forbidden` (`"Bạn không có quyền thực hiện thao tác này."`).
- **EARS-05 (Unwanted):** `IF` yêu cầu thiếu header `Idempotency-Key` hoặc độ dài header vượt quá 128 ký tự, `THEN` hệ thống `SHALL` từ chối thao tác với mã lỗi `400 Bad Request` (`"A valid Idempotency-Key header is required."`).
- **EARS-06 (Optional):** `WHERE` tài khoản bị xóa mang vai trò `TenantAdmin` của một tổ chức, hệ thống `SHALL` ghi nhận Audit Log cảnh báo nhưng giữ nguyên trạng thái của Tenant và các tài khoản nhân viên khác thuộc Tenant đó.

---

## 4. Non-Functional Requirements (NFR)

- **NFR-SEC-01 (Thu hồi Phiên Tức thì):** Toàn bộ `RefreshTokens` của người dùng phải bị xóa/thu hồi trong cùng transaction với việc cập nhật `IsActive = false`.
- **NFR-SEC-02 (Đồng bộ Quyền & Version):** Tăng `AuthorizationVersion` thêm 1 để mọi Access Token JWT cũ bị từ chối ở chu kỳ request tiếp theo.
- **NFR-REL-01 (Bảo đảm Idempotency):** Mọi request xóa phải được điều phối qua `IAuthorizationMutationCoordinator` với `Idempotency-Key` để tránh double-execution khi Client gửi lặp lại do mạng chậm.
- **NFR-DATA-01 (Toàn vẹn Dữ liệu Khóa Ngoại):** Không bao giờ xóa vật lý (`HARD DELETE`) bản ghi `User` để tránh vi phạm ràng buộc khóa ngoại với `AuditLogs`, `FileEntries` và `FileShares`.

---

## 5. Data Model & Error Contract

### 5.1 Command & Response
- **Endpoint:** `DELETE /api/admin/users/{id:guid}`
- **Headers:** `Idempotency-Key: <UUID hoặc chuỗi định danh <= 128 chars>`
- **Command:** `DeleteUserCommand(Guid Id)`
- **Response Model:** `OperationStatusResponse(Guid OperationId, long Version, string Status)` hoặc `Result`

### 5.2 Error Contracts
| Mã lỗi / Tình huống | Kiểu phản hồi | HTTP Status | Nội dung chi tiết |
| :--- | :--- | :--- | :--- |
| Không đủ quyền hạn | `Forbidden` | `403 Forbidden` | `"Bạn không có quyền thực hiện thao tác này."` |
| Thiếu Idempotency-Key | `Result.Failure` | `400 Bad Request` | `"A valid Idempotency-Key header is required."` |
| Không tìm thấy người dùng | `Result.Failure` | `404 Not Found` | `"User with ID '{id}' was not found."` |
| Cấm xóa SystemAdmin duy nhất | `Result.Failure` | `400 Bad Request` | `"Cannot delete the only System Administrator in the system."` |

---

## 6. Acceptance Criteria (BDD Scenarios)

### Kịch bản 1: Xóa mềm người dùng thông thường thành công
```gherkin
Scenario: Xóa mềm tài khoản người dùng thành công
  Given SystemAdmin có quyền "user.manage" đã đăng nhập
  And Tài khoản "huy.nguyen@fpt.com" đang tồn tại và IsActive = true
  When Gửi yêu cầu DELETE /api/admin/users/{id} kèm header "Idempotency-Key: test-uuid-001"
  Then Hệ thống cập nhật IsActive = false cho "huy.nguyen@fpt.com"
  And Thu hồi toàn bộ RefreshTokens liên quan
  And Bản ghi User vẫn được giữ trong CSDL để bảo toàn khóa ngoại
  And Trả về HTTP 200 OK hoặc 202 Accepted
```

### Kịch bản 2: Chặn xóa tài khoản SystemAdmin duy nhất
```gherkin
Scenario: Bảo vệ tài khoản SystemAdmin duy nhất
  Given Hệ thống chỉ có duy nhất 1 tài khoản có vai trò "SystemAdmin" đang hoạt động
  When SystemAdmin gửi yêu cầu xóa tài khoản này
  Then Hệ thống từ chối xóa
  And Trả về lỗi "Cannot delete the only System Administrator in the system." (HTTP 400)
  And Trạng thái của tài khoản vẫn duy trì IsActive = true
```

### Kịch bản 3: Từ chối yêu cầu khi thiếu quyền hạn hoặc thiếu Idempotency Key
```gherkin
Scenario: Chặn yêu cầu xóa không đủ điều kiện
  Given Người dùng không có quyền "user.manage"
  When Gửi yêu cầu DELETE /api/admin/users/{id}
  Then Hệ thống từ chối với mã lỗi HTTP 403 Forbidden
  When Người dùng có quyền nhưng không gửi header "Idempotency-Key"
  Then Hệ thống từ chối với mã lỗi HTTP 400 Bad Request ("A valid Idempotency-Key header is required.")
```

---

## 7. Out of Scope

- Xóa cứng vật lý tài khoản khỏi bảng `Users` (không hỗ trợ vì lý do toàn vẹn khóa ngoại).
- Xóa tổ chức Tenant (được quản lý bởi use case `delete-tenant`).
- Xóa các tệp tin mà người dùng đã tải lên (các tệp tin vẫn được lưu trữ theo chính sách lưu trữ của Tenant).

---

## 8. Feature Lock

- **Locked Scope:** Toàn bộ nghiệp vụ Soft-Delete tài khoản người dùng, bảo vệ SystemAdmin duy nhất, thu hồi Refresh Tokens, và điều phối Idempotent Mutation.
- **Change Path:** `/sdd-update --feature=delete-user --artifact=spec --bump=patch`
- **Lock Boundary:** Feature `delete-user` only.

---

## 9. AI Recommendation và Human Final Review

### AI Recommendation
- **Methodology Profile:** `DETAILED`
- **Status:** `PENDING HUMAN REVIEW`
- **EARS Compliance:** 100% Functional Requirements tuân thủ chuẩn 5 mẫu cú pháp EARS với các nhánh thành công và thất bại đầy đủ.
- **Clean Architecture & Security:** Tuân thủ Zero Secret Policy, bắt buộc Idempotency-Key, bảo vệ Admin duy nhất, thu hồi phiên làm việc tức thì.
- **Đề xuất tiếp theo:** Human reviewer kiểm tra và phê duyệt bản đặc tả SPEC.md cho feature `delete-user`.

```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt bản đặc tả SPEC.md cho feature delete-user.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED in the persisted review block.
```

### Human Final Review
- **Status:** `PENDING`
- **Reviewer:** Pending Authorized Human Reviewer
- **Decision:** Awaiting Human Decision
- **Reviewed At:**
- **Notes / Adjustments:**
