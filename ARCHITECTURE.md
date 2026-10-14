# Kiến trúc Cloud DMS

## Tổng quan

Ứng dụng API .NET8 triển khai chung, giữ bốn tầng:

| Tầng | Trách nhiệm |
|---|---|
| API | Controller, xác thực/policy, middleware, cấu hình và DI. |
| Application | Command/query và handler, DTO/validator, interface tích hợp. |
| Domain | Entity, trạng thái và quy tắc miền. |
| Infrastructure | EF Core/MySQL, Redis bảo mật, storage/S3, ClamAV, search/indexing và adapter HTTP nội bộ. |

Phụ thuộc: API → Application + Infrastructure; Infrastructure → Application + Domain; Application → Domain. Domain độc lập.

## Đã triển khai trong mã

Client → controller → MediatR → logging không chứa dữ liệu nhạy cảm → validation → handler → interface Application → Infrastructure. GlobalExceptionHandlerMiddleware được gắn, lỗi chung/no-store. JWT có session nối Redis; đọc dùng ACL chung và revalidate version/stamp, không lấy claim permission cũ làm fallback khi security unavailable. Các endpoint quản trị chưa chuyển hết sang coordinator, không khẳng định cách ly toàn hệ thống hoàn tất.

SQL giữ metadata, quota, content version, generation/chunk, preview, upload session/intent, team/grant và DocumentOutboxMessages riêng outbox quyền. Object key bất biến theo content version. S3 hỗ trợ multipart/direct presigned read; Local/Drive/OneDrive không giả capability direct URL. Legacy download vẫn tồn tại trong giai đoạn chuyển đổi.

Keyword dùng MySQL full-text + metadata; semantic dùng E5 CPU offline pin revision và Qdrant private. Hybrid RRF kết hợp candidate, SQL/ACL kiểm lại trước trả kết quả. Search session/cursor5 phút lưu Redis, Data Protection key ring bền vững mã hóa certificate và namespace deployment. Không cache signed URL hay tin vector là authority.

Worker scan/type/hash, extract/chunk/embed/stage rồi CAS publish generation; retry/lease/reconcile ngăn worker cũ publish sau overwrite/delete. PDF dùng Poppler child Linux trong bwrap network namespace riêng; thiếu sandbox giữ metadata-only. Preview Office mặc định tắt; converter LibreOffice nội bộ non-root/namespaces/seccomp, không AI/viewer ngoài. TXT/CSV/Markdown và OOXML có extraction giới hạn; không OCR/transcript/HLS.

Upload storage tối đa50 GiB theo cấu hình, analysis100 MiB và scanner coverage riêng. ClamAV coverage mặc định25 MiB, chưa xác nhận; file lớn vẫn lưu chờ xác minh, không read/clean index. Quota logical khác staging inflight; terminal cleanup revisits công bằng vô thời hạn.

Startup mặc định không migration/seed. `Database:InitializeOnStartup=true` opt-in migrate+seed fail closed; không còn DDL vá bảng và nuốt lỗi. Role compatibility migration additive giữ IDs/rows/name uniqueness; legacy Department columns còn giữ dưới shadow mapping; Product phantom đã bỏ khỏi snapshot, không drop bảng legacy.

## Dự kiến kiểm chứng và triển khai

Task12 đã kiểm DB sạch/nâng cấp, unit/legacy, dịch vụ thật và HTTP trong môi trường test riêng; benchmark corpus nhỏ không thay nghiệm thu production. Namespace host chặn converter/PDF; ClamAV engine và pipeline clean ingest đầy đủ chưa kiểm. Xem [kết quả và giới hạn](docs/search-read-validation.md). Dùng [runbook](docs/search-read-runbook.md), mẫu `deploy/compose.search-read.yml`/`search-read.env.example`; compose root cũ không dành production. Rate limiter theo process, cần distributed policy khi nhiều replica. Chưa chốt digest toàn chuỗi image/package hoặc chứng minh resource budget/latency/Recall cho production.
