# SPEC: Get User Storage Quota & Increase Request (Quản lý Quota Cá nhân & Xin cấp thêm Dung lượng)

- **Feature Slug:** `get-user-storage-quota`
- **Module:** `User Portal`
- **Version:** `1.0.0`
- **Status:** `DRAFT`
- **Spec Depth:** `DETAILED`
- **Methodology Profile:**
  - Depth: `DETAILED`
  - Rationale: Giao diện giám sát tài nguyên lưu trữ người dùng và kênh đề xuất điều chỉnh ngân sách Quota phân tầng.
  - Risk Posture: `MEDIUM`
  - Review Route: `Architecture Review`

---

## 1. Context & Goal
Cho phép người dùng (`Employee` / `User`) chuyển sang tab "Dung lượng" ([StorageView.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/user/views/StorageView.tsx)) để theo dõi chi tiết hạn mức Quota cá nhân và tổng quan phòng ban. Đồng thời cung cấp chức năng gửi yêu cầu tăng Quota gửi tới Trưởng phòng ban (`DepartmentAdmin`).

---

## 2. Actors & Roles
- **Actor:** Người dùng có tài khoản hợp lệ trong phòng ban.
- **Unauthorized:** Người dùng chưa đăng nhập bị từ chối (`401 Unauthorized`).

---

## 3. Functional Requirements (EARS)
- **EARS-01 (Event-driven):** `WHEN` người dùng mở tab "Dung lượng", `THEN` hệ thống `SHALL` hiển thị 3 chỉ số chính: Tổng hạn mức được cấp (`Total Allocated`), Dung lượng đã dùng (`Used Space`), và Dung lượng còn lại (`Available Space`) kèm thanh tỷ lệ sử dụng phần trăm.
- **EARS-02 (State-driven):** `WHILE` tỷ lệ tiêu thụ dung lượng:
  - `>= 80%`: Giao diện `SHALL` hiển thị cảnh báo màu vàng cam nhắc nhở sắp hết dung lượng.
  - `>= 95%`: Giao diện `SHALL` hiển thị cảnh báo màu đỏ và thông báo tài khoản sắp bị khóa quyền tải lên.
- **EARS-03 (Event-driven):** `WHEN` người dùng nhấn nút "Yêu cầu tăng dung lượng", nhập số GB cần xin thêm (`requestedGB`) và lý do nghiệp vụ (`reason`) rồi nhấn gửi, `THEN` hệ thống `SHALL` gửi request `POST /api/users/request-quota` và hiển thị thông báo toast: `"Đã gửi đề xuất tăng dung lượng tới Trưởng phòng ban thành công!"`
- **EARS-04 (Unwanted):** `IF` số GB yêu cầu tăng `<= 0` hoặc lý do để trống, `THEN` giao diện `SHALL` chặn gửi yêu cầu và hiển thị thông báo lỗi yêu cầu điền đầy đủ thông tin.

---

## 4. Non-Functional Requirements (NFR)
- **NFR-PERF-01:** Dữ liệu dung lượng hiển thị đồng bộ 100% với giá trị `UserStorageQuota` trong CSDL.

---

## 5. Data Model & Error Contract
- **Endpoint:** `POST /api/users/request-quota`
- **Payload:** `{ requestedGB: number, reason: string }`
- **Response:** `Result<bool>`
- **Errors:** `InvalidQuotaRequest` (400).

---

## 6. Acceptance Criteria (BDD)
### Kịch bản: Gửi yêu cầu tăng Quota thành công
- **Given** Người dùng đã sử dụng 9GB / 10GB (90%).
- **When** Người dùng nhấn yêu cầu tăng thêm 5GB với lý do `"Dự án kiểm toán quý 4 cần tải nhiều báo cáo"`.
- **Then** Request được gửi thành công, thông báo hiển thị cho người dùng và một thông báo nội bộ được gửi tới Trưởng phòng ban.

---

## 7. AI Recommendation và Human Final Review
```text
AI RECOMMENDATION: PENDING HUMAN REVIEW
HUMAN DECISION REQUIRED: Phê duyệt SPEC.md cho feature get-user-storage-quota.
NEXT STEP: Authorized Human reviewer records APPROVED, REVISE, or REJECTED.
```
- **Human Final Review Status:** `PENDING`
