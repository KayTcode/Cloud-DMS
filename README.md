# Cloud-DMS — Enterprise Multi-Tenant Cloud Document Management System

> Hệ thống Quản lý Tài liệu Đám mây Đa Doanh nghiệp (Multi-Tenant Document Management System) xây dựng theo kiến trúc **Clean Architecture** kết hợp mô hình **CQRS (Command Query Responsibility Segregation)**, hỗ trợ phân cấp hạn mức lưu trữ 4 tầng và đa nhà cung cấp đám mây (Google Drive, Microsoft OneDrive, Local Storage).

---

## 🌟 Tính Năng Nổi Bật

- **Kiến trúc Multi-Tenant & RBAC 4 Cấp Độ:**
  - **System Admin**: Quản trị toàn hệ thống, đăng ký và cấu hình các Cloud Storage Provider, tạo Tenant mới, cấp hạn mức lưu trữ (Quota) cho Tenant và giám sát Audit Logs.
  - **Tenant Admin**: Quản trị tổ chức doanh nghiệp, quản lý các phòng ban, chỉ định Trưởng phòng (Department Admin) và phân bổ Quota tổng cho từng phòng ban.
  - **Department Admin**: Thiết lập phòng ban, mời/tạo nhân viên trong phòng ban và phân bổ hạn mức lưu trữ cá nhân cho từng nhân viên.
  - **Employee**: Tải lên, tải xuống, tìm kiếm phân trang và quản lý tài liệu cá nhân trong phạm vi dung lượng được cấp.

- **Mô Hình Quản Lý Hạn Mức Lưu Trữ 4 Tầng (Hierarchical Quota Management):**
  `Storage Provider (Pool Tổng) ➔ Tenant Quota (Gói Doanh Nghiệp) ➔ Department Quota (Phòng Ban) ➔ User Storage Quota (Cá Nhân)`
  Đảm bảo kiểm soát chặt chẽ dung lượng không vượt trần ở bất kỳ cấp độ nào.

- **Bảo Mật & Kiểm Soát Rủi Ro Mức Doanh Nghiệp:**
  - **Chống Race Condition Quota Overcommit**: Áp dụng cơ chế **Hai Pha (Two-Phase Verification)** kết hợp Transaction mức cô lập `IsolationLevel.Serializable` và `ExecutionStrategy.ExecuteAsync`. Tự động bồi hoàn dọn dẹp file Cloud nếu phát hiện vi phạm dung lượng khi upload đồng thời.
  - **Chống Brute-force & DoS (Rate Limiting)**: Chính sách giới hạn 5 requests/phút cho Authentication và 30 requests/phút cho File Uploads.
  - **JWT 15 Phút + Refresh Token Rotation**: Access Token có hạn ngắn 15 phút, tự động xoay vòng Refresh Token (TTL 7 ngày) trên Axios Interceptor mà không làm gián đoạn trải nghiệm người dùng; tự động thu hồi session khi đổi mật khẩu/khóa tài khoản.
  - **Quét Mã Độc Tự Động (ClamAV)**: Kiểm tra virus theo giao thức TCP INSTREAM trước khi lưu trữ, áp dụng cơ chế **Fail-Closed** ở môi trường Production.
  - **Security Headers & CORS**: Bảo vệ chống XSS, Clickjacking, MIME-sniffing (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`) và cấu hình CORS chặt chẽ theo `Cors:AllowedOrigins`.

---

## 🛠️ Công Nghệ Sử Dụng

### Backend
- **Framework**: .NET 8 (C# 12), ASP.NET Core Web API
- **Kiến trúc**: Clean Architecture (Domain, Application, Infrastructure, API)
- **Design Pattern**: CQRS với **MediatR**, FluentValidation
- **ORM & Database**: Entity Framework Core 8, MySQL / MariaDB (Pomelo provider)
- **Bảo mật & Token**: ASP.NET Core Identity Tokens, JWT Bearer, BCrypt.Net
- **Cloud Integrations**: Google Drive API v3, Microsoft Graph API, MailKit (SMTP)

### Frontend
- **Framework**: React 19, TypeScript
- **Tooling**: Vite, TailwindCSS, Lucide Icons
- **HTTP Client**: Axios với Interceptor tự động Refresh Token và xử lý lỗi đồng bộ

---

## 🏛️ Cấu Trúc Dự Án (Clean Architecture)

```
Cloud-DMS/
├── src/
│   ├── CleanArchCqrs.Domain/           # Core Entities (Tenant, User, Department, FileEntry, Quota...)
│   ├── CleanArchCqrs.Application/      # CQRS Commands & Queries, DTOs, Behaviors, Interfaces
│   ├── CleanArchCqrs.Infrastructure/   # EF Core DbContext, ClamAV, EmailService, Cloud Storage Services
│   └── CleanArchCqrs.API/              # Controllers, Middlewares, Rate Limiter, Program.cs
├── ../Cloud-DMS-FE/                    # Checkout Frontend React Vite cùng cấp
│   ├── src/
│   │   ├── features/                   # Modules: auth, system-admin, tenants, department, user
│   │   ├── api/                        # axiosClient & refresh token interceptor
│   │   └── layouts/                    # MainLayout, Sidebar, Navbar
├── docs/                               # Tài liệu đặc tả phân quyền và API (00 -> 04)
└── plan/                               # Kế hoạch phát triển, kiểm nghiệm và theo dõi bug
```

---

## 🚀 Hướng Dẫn Cài Đặt & Khởi Chạy

### 1. Yêu Cầu Môi Trường
- .NET 8.0 SDK trở lên
- Node.js 18+ & npm
- MySQL Server (Port 3306)
- ClamAV Daemon (tùy chọn trong môi trường Development, cổng 3310)

### 2. Cấu Hình Backend
Chỉnh sửa cấu hình chuỗi kết nối và thông tin SMTP trong `src/CleanArchCqrs.API/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=CloudDmsDb;User=root;Password=your_password;CharSet=utf8mb4;"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:5173"
    ]
  },
  "ClamAV": {
    "Host": "127.0.0.1",
    "Port": 3310,
    "Enabled": true
  }
}
```

### 3. Chạy Backend API (Local Development)
```bash
cd Cloud-DMS
dotnet restore
dotnet run --project src/CleanArchCqrs.API
```
- Khi chạy ở môi trường `Development`, hệ thống sẽ tự động thực thi Migration và khởi tạo dữ liệu RBAC + tài khoản demo System Admin mặc định:
  - **Tài khoản**: `admin@clouddms.com`
  - **Mật khẩu**: `Admin@123`
- Swagger UI tài liệu API: `http://localhost:5000/` hoặc `https://localhost:5001/`
- Kiểm tra trạng thái hệ thống: `GET http://localhost:5000/health` (Healthy)

### 4. Khởi Chạy Full-stack Local trên Windows

Chuẩn bị Docker Desktop, Node.js/npm và checkout frontend ở thư mục cùng cấp `../Cloud-DMS-FE`. Frontend phải có file `.env` cục bộ đúng URL API; không commit file này. Cấu hình backend cục bộ cũng phải được cấp ngoài mã nguồn theo quy trình của môi trường. Compose local dùng `Development`, CORS `http://localhost:3000` và giữ `Database:InitializeOnStartup=false`; khởi tạo schema cần thực hiện riêng theo quy trình được duyệt.

Từ bất kỳ thư mục hiện hành nào, gọi script bằng đường dẫn đến file; script tự xác định repository, kiểm tra Docker/npm/frontend và chạy backend cùng dependencies bằng Compose trước khi chạy `npm start` trong frontend:

```powershell
& 'C:\đường-dẫn\Cloud-DMS\deploy\Start-Local.ps1'
```

Khi đang ở checkout backend, lệnh ngắn là:

```powershell
.\deploy\Start-Local.ps1
```

`npm start` chạy ở foreground; nhấn `Ctrl+C` trong cửa sổ đó để dừng frontend. Sau đó dừng backend/dependencies mà không xóa container, volume hoặc image:

```powershell
.\deploy\Stop-Local.ps1
```

`deploy/compose.local.yml` chỉ dùng cho local development. Triển khai search/read dùng `deploy/compose.search-read.yml` và runbook riêng.

### 5. Chạy Kiểm Thử Tự Động (Automated Testing)
Bộ kiểm thử tích hợp 20 kịch bản Unit & Integration test bảo vệ Quota, IDOR, phân quyền và bảo mật:
```bash
dotnet test tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj
```

### 6. Chạy Frontend Thủ Công
Từ checkout cùng cấp `../Cloud-DMS-FE`, sau khi đã cài dependencies của frontend theo hướng dẫn riêng, chạy `npm start`. Script ở mục 4 thực hiện lệnh này tự động.

---

## 📚 Tài Liệu Đặc Tả & Báo Cáo Dự Án

- **Đặc tả Phân hệ & Nghiệp vụ:**
  - [00. Tổng quan hệ thống và Quota](./docs/00_TONG_QUAN_HE_THONG_VA_QUOTA.md)
  - [01. Phân hệ System Admin](./docs/01_ROLE_SYSTEM_ADMIN.md)
  - [02. Phân hệ Tenant Admin](./docs/02_ROLE_TENANT_ADMIN.md)
  - [03. Phân hệ Department Admin](./docs/03_ROLE_DEPARTMENT_ADMIN.md)
  - [04. Phân hệ Nhân viên (Employee)](./docs/04_ROLE_EMPLOYEE.md)
- **Kế hoạch & Báo cáo Chất lượng:**
  - [Báo cáo Tổng hợp & Review Toàn diện](./review/00_REVIEW_TOAN_BO.md)
  - [Báo cáo Benchmark & Tải 100k Users](./review/02_BENCHMARK.md)
  - [Kế hoạch & Danh sách Task Chi tiết](./plan/07_DANH_SACH_TASK.md)

---
© 2026 Cloud-DMS Engineering Team. All rights reserved.

