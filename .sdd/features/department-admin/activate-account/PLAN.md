# SDD Phase 2: Architecture Plan — Department Admin Activate Account

- **Feature Slug:** `activate-account`
- **Module:** `DepartmentAdmin`
- **Version:** `1.0.0`
- **Status:** `PENDING HUMAN REVIEW`
- **Inherited Spec Depth:** `DETAILED`
- **Risk Posture:** `HIGH` (Bảo mật tài khoản, xác thực mật khẩu, điều hướng Google OAuth Callback)

---

## 1. Architectural Approach & Pattern

- **Kiến trúc áp dụng:** Clean Architecture & CQRS (Command Query Responsibility Segregation).
- **Nguyên tắc định hướng:**
  1. *Fix the Spec, not the Code workaround:* Xác định rõ vai trò `DepartmentAdmin` ngay từ hợp đồng `ValidateInvitationQuery` và DTO phản hồi, không hardcode trên giao diện client.
  2. *Origin Preservation:* Tách biệt ranh giới giữa Backend API Server (`http://localhost:5289`) và Frontend SPA Client (`http://localhost:3000` / `http://localhost:5173`). Mọi chuyển hướng sau xác thực ngoài (Google OAuth) phải trả về Frontend Origin với token query parameters, tuyệt đối không redirect tương đối dẫn vào Swagger UI (`localhost:5289/`).
  3. *Single-Use Token Lifecycle:* Token kích hoạt trong `EmailVerificationTokens` bị vô hiệu hóa (`IsUsed = true`) ngay khi kích hoạt thành công để ngăn chặn tấn công replay.

---

## 2. REQ-to-Component Mapping

| Requirement (SPEC.md) | Component phụ trách | Tầng (Layer) | File path | Trách nhiệm chính |
| :--- | :--- | :--- | :--- | :--- |
| **EARS-01** (Validate Token & Role/Department) | `ValidateInvitationQuery`, `ValidateInvitationQueryHandler` | Application | `CleanArchCqrs.Application/Users/Queries/ValidateInvitation/ValidateInvitationQuery.cs` | Xác thực token, lấy Email, FullName, TenantName, Role, DepartmentName |
| **EARS-01** (Contract) | `InvitationValidationDto` | Application (DTOs) | `CleanArchCqrs.Application/Users/DTOs/InvitationValidationDto.cs` | Chứa dữ liệu phản hồi kiểm tra mã kích hoạt |
| **EARS-02, EARS-03** (Accept Invitation) | `AcceptInvitationCommand`, `AcceptInvitationCommandHandler` | Application | `CleanArchCqrs.Application/Users/Commands/AcceptInvitation/AcceptInvitationCommand.cs` | Kích hoạt User (`IsActive = true`, `EmailConfirmed = true`), đổi mật khẩu, cấp JWT |
| **EARS-04** (Google OAuth URL & Sanitization) | `GoogleAuthService` | Infrastructure | `CleanArchCqrs.Infrastructure/Services/Auth/GoogleAuthService.cs` | Lưu `returnUrl` an toàn trong session cache, bảo toàn Scheme/Host/Port loopback |
| **EARS-04** (Google Login & Callback Endpoint) | `AuthController` | API (Interface Adapter) | `CleanArchCqrs.API/Controllers/AuthController.cs` | Nhận `Referer` xác định Frontend Origin, ghép query parameters và redirect về Frontend |
| **EARS-04** (OAuth SPA Receiver) | `GoogleOAuthCallback.tsx` | Frontend (Feature Auth) | `Cloud-DMS-FE/src/features/auth/pages/GoogleOAuthCallback.tsx` | Nhận token từ URL callback, lưu localStorage và chuyển hướng vào Dashboard |
| **NFR-UX-01** (Dynamic Role UI) | `AcceptInvitationPage.tsx` | Frontend (Feature Auth) | `Cloud-DMS-FE/src/features/auth/pages/AcceptInvitationPage.tsx` | Hiển thị động nhãn "Quản trị viên Phòng ban", tên phòng ban, và nút kích hoạt tương ứng |
| **NFR-UX-01** (Frontend Types) | `authApi.ts` | Frontend (API Adapter) | `Cloud-DMS-FE/src/features/auth/api/authApi.ts` | Cập nhật `InvitationValidationResponse` có `role`, `departmentName`, `tokenType` |

---

## 3. Data Flow Architecture

```
[Trưởng phòng nhận Email]
         │
         ▼
[Mở link: /accept-invitation?token={T}&email={E}]
         │
         ├──► 1. GET /api/auth/validate-invitation?token={T}&email={E}
         │       └── ValidateInvitationQueryHandler ──► DB (EmailVerificationTokens + Users + Departments)
         │       ◄── Trả về { isValid, role: 'DepartmentAdmin', departmentName: 'CNTT', tenantName: 'FPT' }
         │
         ├──► 2A. Tự đặt mật khẩu / Kích hoạt nhanh:
         │       └── POST /api/auth/accept-invitation
         │       └── AcceptInvitationCommandHandler ──► Cập nhật DB + Trả về JWT Session
         │       └── Chuyển hướng Frontend SPA -> / (DepartmentAdminPortal)
         │
         └──► 2B. Kích hoạt & Đăng nhập Google:
                 ├── POST /api/auth/accept-invitation (Kích hoạt trước tài khoản)
                 ├── GET /api/auth/google/login?returnUrl={FrontendOrigin}/auth/google/callback
                 │       └── GoogleAuthService lưu returnUrl đầy đủ vào In-Memory Cache
                 ├── Chuyển hướng tới Google Consent Screen
                 ├── Google chuyển hướng về Backend: GET /api/auth/google/callback?code=...
                 └── Backend AuthController giải mã returnUrl:
                         └── Redirect({FrontendOrigin}/auth/google/callback?accessToken=...&role=DepartmentAdmin...)
                         └── GoogleOAuthCallback.tsx lưu session -> Mở Dashboard SPA
```

---

## 4. State-Change Classification & Checkpoints

| Luồng / Thay đổi | Phân loại State-Change | Yêu cầu Checkpoint |
| :--- | :--- | :--- |
| Mở rộng `InvitationValidationDto` | `shared/public contract` | Non-breaking (thêm trường optional), tương thích ngược với Tenant Admin cũ. |
| Kích hoạt tài khoản (`AcceptInvitation`) | `persistence schema / business-data mutation` | Cập nhật `Users` và `EmailVerificationTokens`, tạo Refresh Token. |
| Chuẩn hóa URL Google OAuth | `permission / security / runtime configuration` | Đảm bảo chặn Open Redirect (chỉ chấp nhận whitelist Loopback hoặc Configured Frontend Base Url). |
| Điều chỉnh giao diện `AcceptInvitationPage` | `none` (Client UI presentation) | Build client kiểm tra bundle size và type checking. |

---

## 5. Chi tiết các bước điều tra và khắc phục lỗi (Action Record)

### Bước 1: Khảo sát nguyên nhân gốc rễ (Root Cause Analysis)
- **Hiện tượng:** Khi kích hoạt tài khoản Trưởng phòng ban (đặc biệt qua Google OAuth), trình duyệt bị rơi vào giao diện Swagger UI (`http://localhost:5289/`).
- **Phát hiện:**
  - `AcceptInvitationPage.tsx` truyền tham số `getGoogleLoginUrl('/')`.
  - `GoogleAuthService.SanitizeReturnUrl` chuyển URI `http://localhost:5173/` thành `/` (bỏ Scheme/Host/Port).
  - `AuthController.cs` nhận `result.ReturnUrl = '/'`, gọi `Redirect('/')` dẫn tới root của cổng máy chủ Backend 5289.
  - `Program.cs` cấu hình `app.UseSwaggerUI(c => c.RoutePrefix = string.Empty)` nên trang chủ Backend 5289 chính là Swagger UI.
  - Ngoài ra, giao diện kích hoạt bị hardcode tiêu đề là "Tenant Admin" thay vì "Department Admin".

### Bước 2: Thiết kế và cập nhật Backend Contracts
- Cập nhật [InvitationValidationDto.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Users/DTOs/InvitationValidationDto.cs) bổ sung các trường: `Role`, `DepartmentName`, `TokenType`.
- Cập nhật [ValidateInvitationQuery.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Application/Users/Queries/ValidateInvitation/ValidateInvitationQuery.cs) truy vấn thông tin User để trả về chính xác vai trò và phòng ban được phân bổ.

### Bước 3: Sửa lỗi bảo toàn Frontend Origin và Google OAuth Redirect
- Trong [GoogleAuthService.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.Infrastructure/Services/Auth/GoogleAuthService.cs), sửa hàm `SanitizeReturnUrl` để giữ nguyên `uri.ToString()` đối với các loopback URI (`localhost` / `127.0.0.1`), không cắt bỏ Scheme và Port.
- Trong [AuthController.cs](file:///d:/Ky%209/DoAn_2/Cloud-DMS/src/CleanArchCqrs.API/Controllers/AuthController.cs):
  - Phương thức `GoogleLogin`: Nếu `returnUrl` tương đối hoặc rỗng, đọc header `Referer` của request để xác định chính xác Frontend Origin (ví dụ `http://localhost:5173` hoặc `http://localhost:3000`).
  - Phương thức `GoogleCallback`: Chuẩn hóa `safeReturnUrl`, gắn kèm các tham số token (`accessToken`, `refreshToken`, `userId`, `role`, `tenantId`, `permissions`) và chuyển hướng về `{FrontendOrigin}/auth/google/callback`.

### Bước 4: Cập nhật giao diện động theo vai trò trên Frontend
- Cập nhật [authApi.ts](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/auth/api/authApi.ts) đồng bộ kiểu dữ liệu `InvitationValidationResponse`.
- Trong [AcceptInvitationPage.tsx](file:///d:/Ky%209/DoAn_2/Cloud-DMS-FE/src/features/auth/pages/AcceptInvitationPage.tsx):
  - Chuyển `getGoogleLoginUrl('/')` thành `getGoogleLoginUrl(`${window.location.origin}/auth/google/callback`)`.
  - Bổ sung logic `roleMeta` để tự động render tiêu đề:
    - `DepartmentAdmin` -> *"Kích hoạt Quản trị viên Phòng ban"* kèm badge và dòng hiển thị tên phòng ban.
    - `Employee` -> *"Kích hoạt Tài khoản Nhân viên"*.
    - `TenantAdmin` -> *"Kích hoạt Quản trị viên Tổ chức"*.
  - Cập nhật thông điệp hoàn tất thành công: *"Đang chuyển hướng tới Cổng Quản trị Phòng ban..."*.

### Bước 5: Kiểm định và xác nhận (Verification)
- Chạy `npm run build` trên thư mục `Cloud-DMS-FE` -> Kết quả thành công 100% (0 lỗi TypeScript / bundling).
- Kiểm tra tính toàn vẹn cú pháp C# trên `Cloud-DMS` solution (Domain, Application, Infrastructure đều biên dịch hoàn tất).

---

## 6. Consistency Map

| Yêu cầu | Thành phần Plan | Task dự kiến | Lệnh kiểm định | Bằng chứng kiểm tra |
| :--- | :--- | :--- | :--- | :--- |
| **EARS-01** | `ValidateInvitationQuery` | Trả về `Role` & `DepartmentName` | `dotnet build` | Phản hồi API có `Role = 'DepartmentAdmin'` |
| **EARS-04** | `GoogleAuthService` & `AuthController` | Bảo toàn Frontend Origin, redirect về `/auth/google/callback` | `dotnet build` | Google login chuyển hướng về port 5173/3000, không về 5289 |
| **NFR-UX-01** | `AcceptInvitationPage.tsx` | Hiển thị động thông tin Department Admin | `npm run build` | Bundle Vite thành công, trang hiển thị đúng phòng ban |

---

## 7. Rủi ro Kỹ thuật & Biện pháp Giảm thiểu (Risks & Mitigations)

1. **Rủi ro Open Redirect Attack:**
   - *Nguy cơ:* Kẻ tấn công lợi dụng tham số `returnUrl` để chuyển hướng người dùng đến trang độc hại sau khi đăng nhập Google.
   - *Giảm thiểu:* Hàm `SanitizeReturnUrl` kiểm tra nghiêm ngặt `uri.IsLoopback` (cho môi trường Dev) hoặc khớp chính xác với `EmailSettings:FrontendBaseUrl` cấu hình trong hệ thống.
2. **Rủi ro Token Replay:**
   - *Nguy cơ:* Người dùng dùng lại link email cũ sau khi đã kích hoạt để chiếm phiên.
   - *Giảm thiểu:* Trường `IsUsed = true` được cập nhật ngay trong transaction đầu tiên. Các yêu cầu tiếp theo trả về `400 Bad Request` yêu cầu đăng nhập trực tiếp.
3. **Rủi ro Khác biệt cổng Frontend giữa các môi trường Dev (3000 vs 5173):**
   - *Nguy cơ:* Máy dev này chạy Vite port 5173, máy dev khác chạy 3000 gây lệch cổng chuyển hướng.
   - *Giảm thiểu:* Sử dụng dynamic `window.location.origin` trên Client và fallback đọc `Request.Headers.Referer` trên Backend để tự động nhận diện chính xác cổng đang phục vụ client.

---

## 8. Questions for Human Reviewer

### Q1: Hành vi sau khi Trưởng phòng ban kích hoạt thành công lần đầu
- **Điều chưa rõ:** Spec chưa xác định cụ thể sau khi đăng nhập thành công, Trưởng phòng ban sẽ vào Dashboard tổng quan phòng ban ngay hay cần mở một Wizard thiết lập phòng ban ban đầu (Department Setup Wizard).
- **Assumption hiện tại:** Kích hoạt xong sẽ tự động chuyển hướng về `/` (Cổng Quản trị Phòng ban `DepartmentAdminPortal`).
- **Ảnh hưởng nếu assumption sai:** Cần bổ sung route điều hướng đặc thù (ví dụ `/department/setup`) nếu có bước onboard bắt buộc.
- **Câu hỏi cụ thể:** Trưởng phòng ban có cần flow Onboarding Wizard bắt buộc sau lần đăng nhập đầu tiên không?

---

## 9. AI Agent Recommendation

- **Status:** PENDING HUMAN REVIEW
- **Scope:** Plan kiến trúc và toàn bộ các bước khắc phục lỗi kích hoạt tài khoản Department Admin.
- **Recommendation:** Phê duyệt Plan kỹ thuật trên; tiếp tục kiểm thử tích hợp luồng gửi email thực tế sau khi khởi động lại Backend API trong Visual Studio.
- **Evidence:**
  - `Cloud-DMS-FE`: `npm run build` thành công `✓ built in 1.09s`.
  - Backend: `InvitationValidationDto`, `ValidateInvitationQuery`, `GoogleAuthService`, `AuthController` đã được cập nhật đồng bộ.
- **Risks and assumptions:** Giả định môi trường frontend chạy trên localhost (cổng 3000 hoặc 5173) đã được cấu hình trong CORS whitelist.
- **Alternatives considered:** Giữ nguyên `returnUrl` cố định đọc từ `appsettings.json` -> Bị loại bỏ vì thiếu linh hoạt khi lập trình viên đổi cổng chạy Vite (`localhost:5173`).
- **Required human decision:** Human reviewer phê duyệt phương án chuyển hướng dynamic origin và nội dung hiển thị cho Department Admin.

---

## 10. Human Final Review

- **Status:** PENDING
- **Decision:** 
- **Reviewer:** 
- **Reviewed at:** 
- **Follow-up:** 
